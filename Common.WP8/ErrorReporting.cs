﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using Microsoft.Phone.Info;
using Microsoft.Phone.Tasks;

namespace Common.WP8
{
    public static class ErrorReporting
    {
        private const string ErrorReportFile = "ErrorReporting.txt";

        private static string ToString(DateTime d)
        {
            return d.TimeOfDay.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
        }

        public static void ReportException(Exception ex, string header)
        {
            try
            {
                var content = "[" + ToString(DateTime.UtcNow) + "]" + " " + header;
                if (ex != null)
                {
                    content += "\n" + ex.ToString();
                }
                content += "\n";
                IsolatedStorage.AppendText(ErrorReportFile, content);
            }
            catch { }
        }

        private static List<Tuple<DateTime, string>> log = new List<Tuple<DateTime, string>>();

        public static void Log(string message)
        {
            log.Add(Tuple.Create(DateTime.UtcNow, message));
        }

        public static void CheckForPreviousException(bool startingUp)
        {
            string contents = null;
            try
            {
                contents = IsolatedStorage.ReadAllText(ErrorReportFile);
                IsolatedStorage.Delete(ErrorReportFile);
            }
            catch { }
            if (contents != null)
            {
                var text = "A problem occurred" + (startingUp ? " the last time you ran this application" : "") + ". Would you like to send an email to report it?";
                if (Extensions.ShowMessageBox("Problem Report", text, "Send report", "No thanks"))
                {
                    var email = new EmailComposeTask();
                    email.To = AppMetadata.Current.Email;
                    email.Subject = AppMetadata.Current.Name + " " + AppMetadata.Current.Version + " auto-generated problem report";
                    email.Body = GetMailBody(contents);
                    email.Show();
                }
            }
        }

        public static string GetMailBody(string contents)
        {
            if (string.IsNullOrEmpty(contents))
            {
                if (AppMetadata.Current.GetExtraErrorReportingInfo != null)
                {
                    var extraInfo = AppMetadata.Current.GetExtraErrorReportingInfo();
                    if (!string.IsNullOrEmpty(extraInfo))
                    {
                        contents = extraInfo + "\n";
                    }
                }
            }

            if (log.Count > 0)
            {
                contents += "---------------------------------";
                foreach (var item in log)
                {
                    contents += "\n[" + ToString(item.Item1) + "]" + " " + item.Item2;
                }
            }
            if (contents.Length > 32000)
            {
                contents = contents.Substring(0, 32000) + " ...";
            }
            return "Write your feedback here\n\n" + contents + "\n---------------------------------\n" +
                   "App Version: " + AppMetadata.Current.Version + "\n" +
                   "OS Version: " + Environment.OSVersion.Version + "\n" +
                   "Phone: " + DeviceStatus.DeviceManufacturer + " " + DeviceStatus.DeviceName + " " + DeviceStatus.DeviceHardwareVersion + " " + DeviceStatus.DeviceFirmwareVersion + "\n" +
                   "Theme: " + ((Visibility)Application.Current.Resources["PhoneLightThemeVisibility"] == Visibility.Visible ? "Light" : "Dark") + "\n" +
                   "Culture: " + CultureInfo.CurrentCulture + "/" + CultureInfo.CurrentUICulture + "\n" +
                   (AppMetadata.Current.UsesLocation ? "Location Services Enabled: " + Settings.GetBool(Setting.LocationServicesEnabled) + "\n" : "") +
                   "---------------------------------";
        }
    }
}