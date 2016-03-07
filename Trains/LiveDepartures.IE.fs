module Trains.LiveDepartures.IE

open System
open System.Net
open System.Threading
open FSharp.Control
open FSharp.Data
open Trains

//http://api.irishrail.ie/realtime/index.htm
type private LocationType =
    | Origin
    | Stop
    | TimingPoint //non stopping location
    | Destination
    override x.ToString() =
        match x with
        | Origin -> "O"
        | Stop -> "S"
        | TimingPoint -> "T"
        | Destination -> "D"

type private StationDataXmlT = XmlProvider<"IrelandStationData.xml">
type private TrainMovementsStationDataXmlT = XmlProvider<"IrelandTrainMovements.xml">

let private xmlToJourneyElement (xml:TrainMovementsStationDataXmlT.ObjTrainMovements) =
    let hasDeparted = xml.Departure.IsSome
    let delayedMins = int (if xml.LocationType = LocationType.Origin.ToString()
                           then xml.ExpectedDeparture - xml.ScheduledDeparture
                           else xml.ExpectedArrival - xml.ScheduledArrival).TotalMinutes
    { Arrives = Time.Create (if xml.LocationType = LocationType.Origin.ToString()
                             then xml.ScheduledDeparture
                             else xml.ScheduledArrival) |> Some
      Station = xml.LocationFullName
      Status = if delayedMins > 0 then Delayed(hasDeparted, delayedMins) else OnTime(hasDeparted)
      Platform = None
      IsAlternateRoute = false }

let private getJourneyDetails trainCode trainDate = async {

    let url = sprintf "http://api.irishrail.ie/realtime/realtime.asmx/getTrainMovementsXML?TrainId=%s&TrainDate=%s" trainCode trainDate
    let! xml = Http.AsyncRequestString url
    let xmlT = TrainMovementsStationDataXmlT.Parse xml

    let getDepartures() = 
        try 
            xmlT.ObjTrainMovements
            |> Array.filter (fun xml -> xml.LocationType <> LocationType.TimingPoint.ToString())
            |> Array.map xmlToJourneyElement
        with 
        | exn -> raise <| ParseError(sprintf "Failed to parse departures xml from %s:\n%s" url xml, exn)

    return getDepartures() 
}

let private xmlToDeparture callingAtFilter (xml:StationDataXmlT.ObjStationData) =

    let propertyChangedEvent = Event<_,_>()

    String.trim xml.Traincode, 
    ({ Due = Time.Create xml.Schdepart
       Destination = xml.Destination
       DestinationDetail = ""
       Status = if xml.Late > 0 then Status.Delayed xml.Late else Status.OnTime
       Platform = None
       Details = LazyAsync.fromAsync (getJourneyDetails (String.trim xml.Traincode) <| xml.Traindate.ToString("dd MMM yyyy"))
       Arrival = ref None
       PropertyChangedEvent = propertyChangedEvent.Publish }, callingAtFilter, propertyChangedEvent)

let private xmlToArrival callingAtFilter (xml:StationDataXmlT.ObjStationData) =
    String.trim xml.Traincode,
    { Due = Time.Create xml.Scharrival
      Origin = xml.Origin
      Status = if xml.Late > 0 then Status.Delayed xml.Late else Status.OnTime
      Platform = None
      Details = LazyAsync.fromAsync (getJourneyDetails (String.trim xml.Traincode) <| xml.Traindate.ToString("dd MMM yyyy")) }

let private getCallingPoints (tr:HtmlNode, table:HtmlNode) =
    let trainId = 
        tr 
        |> HtmlNode.elementsNamed ["td" ]
        |> List.head 
        |> HtmlNode.elementsNamed ["a"]
        |> List.head
        |> Html.innerText
    let callingPoints = 
        table
        |> HtmlNode.elementsNamed ["tr"]
        |> Seq.filter (HtmlNode.hasId ("train" + trainId))
        |> Seq.collect (HtmlNode.descendantsNamed false ["tr"])
        |> Seq.filter (HtmlNode.hasClass "")
        |> Seq.map (fun tr -> let cells = tr |> HtmlNode.elementsNamed ["td"]
                              let station = cells.[1] |> Html.innerText
                              station)
        |> Set.ofSeq
    trainId, callingPoints

let private getDeparturesOrArrivals forDepartures mapper getOutput (departuresAndArrivalsTable:DeparturesAndArrivalsTable) = 

    let xmlUrl = "http://api.irishrail.ie/realtime/realtime.asmx/getStationDataByCodeXML?StationCode=" + departuresAndArrivalsTable.Station.Code

    let getDeparturesOrArrivals callingAtFilter xml extraFilter =
        try 
            let xmlT = StationDataXmlT.Parse xml
            xmlT.ObjStationDatas
            |> Array.filter (fun xml -> xml.Locationtype <> (if forDepartures then LocationType.Destination else LocationType.Origin).ToString())
            |> Array.map (mapper callingAtFilter)
            |> extraFilter
            |> Array.mapi getOutput
        with 
        | exn -> raise <| ParseError(sprintf "Failed to parse xml from %s:\n%s" xmlUrl xml, exn)
    
    let htmlUrl = "http://www.irishrail.ie/realtime/station_details.jsp?ref=" + departuresAndArrivalsTable.Station.Code
    
    let getCallingPointsByTrain html =
        let (&&&) f1 f2 arg = f1 arg && f2 arg
        try 
            html
            |> HtmlDocument.Parse
            |> HtmlDocument.descendantsNamed false ["table"]
            |> Seq.collect (fun table -> table |> HtmlNode.elementsNamed ["tr"] |> List.map (fun tr -> tr, table))
            |> Seq.filter (fst >> (HtmlNode.hasClass "" &&& HtmlNode.hasId ""))
            |> Seq.map getCallingPoints
            |> Map.ofSeq
        with 
        | exn when html.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) > 0 ||
                   html.IndexOf("WiFi", StringComparison.OrdinalIgnoreCase) > 0 ->
            raise <| new WebException()
        | exn -> raise <| ParseError(sprintf "Failed to parse departures html from %s:\n%s" htmlUrl html, exn)

    match departuresAndArrivalsTable.CallingAt with
    | None ->

        async {
            let! xml = Http.AsyncRequestString xmlUrl
            return getDeparturesOrArrivals None xml id
        }

    | Some callingAt ->

        async {
            
            let! html = Http.AsyncRequestString htmlUrl
            let html = Html.clean html

            let callingPointsByTrainId = getCallingPointsByTrain html
            
            let getCallingPoints trainId =
                match Map.tryFind trainId callingPointsByTrainId with
                | Some value -> value
                | _ -> Set.empty

            let! xml = Http.AsyncRequestString xmlUrl

            let extraFilter =
                Array.filter (fst >> getCallingPoints >> Set.contains callingAt.Name)
         
            return getDeparturesOrArrivals (Some callingAt.Name) xml extraFilter

        }

let getDepartures departuresAndArrivalsTable = 
    
    let synchronizationContext = SynchronizationContext.Current

    async {

        let! token = Async.CancellationToken

        let getDeparture i (trainId, (departure:Departure, callingAtFilter, propertyChangedEvent)) = 
            if (!departure.Arrival).IsNone then
                // only fetch the arrival time for the first 10 departures
                if i < 10 then
                    departure.SubscribeToDepartureInformation callingAtFilter propertyChangedEvent synchronizationContext token
            departure

        return! getDeparturesOrArrivals true xmlToDeparture getDeparture departuresAndArrivalsTable
    } |> LazyAsync.fromAsync

let getArrivals departuresAndArrivalsTable = 

    getDeparturesOrArrivals false xmlToArrival (fun _ (_, arrival) -> arrival) departuresAndArrivalsTable
    |> LazyAsync.fromAsync
