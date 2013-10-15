﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Coursera;
using FSharp.Control;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Tasks;

namespace LearnOnTheGo
{
    public partial class CoursePage : PhoneApplicationPage
    {
        public CoursePage()
        {
            InitializeComponent();
            CommonApplicationBarItems.Init(this);
        }

        private int courseId;
        private LazyBlock<LectureSection[]> lecturesLazyBlock;
        private LazyBlock<string> videoLazyBlock;
        private bool userInteracted;
        private string lastEmail;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            courseId = int.Parse(NavigationContext.QueryString["courseId"]);

            // settings changed
            // settings changed
            if (lastEmail != null && lastEmail != Settings.GetString(Setting.Email))
            {
                LittleWatson.Log("Settings changed");
                pivot.ItemsSource = null;
            }

            lastEmail = Settings.GetString(Setting.Email);

            // app was tombstoned or settings changed
            if (App.Crawler == null)
            {
                LittleWatson.Log("App.Crawler is null");

                var email = Settings.GetString(Setting.Email);
                var password = Settings.GetString(Setting.Password);
                if ((string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)))
                {
                    SafeGoBack();
                    return;
                }

                App.Crawler = new Crawler(email, password, Cache.GetFiles(), Cache.SaveFile, DownloadInfo.Create);
                new LazyBlock<Course[]>(
                    "courses",
                    "No courses",
                    App.Crawler.Courses,
                    a => a.Length == 0,
                    new LazyBlockUI<Course[]>(
                        this,
                        _ => Setup(),
                        () => false,
                        messageTextBlock),
                    false,
                    null,
                    success =>
                    {
                        if (!success)
                        {
                            LittleWatson.Log("Failed to get courses");
                            App.Crawler = null;
                            SafeGoBack();
                        }
                    },
                    null);
            }
            else
            {
                Setup();
            }
        }

        private void Setup()
        {
            if (!App.Crawler.HasCourse(courseId))
            {
                LittleWatson.Log("App.Crawler.HasCourse is false");
                SafeGoBack();
                return;
            }

            var course = App.Crawler.GetCourse(courseId);
            pivot.Title = course.Topic.Name;
            LittleWatson.Log(courseId + " = " + course.Topic.Name + " [" + course.Name + "]");

            if (pivot.ItemsSource == null)
            {
                Load(false);
            }
            else
            {
                foreach (var lectureSection in pivot.ItemsSource.Cast<LectureSection>())
                {
                    foreach (var lecture in lectureSection.Lectures)
                    {
                        lecture.DownloadInfo.RefreshStatus();
                    }
                }
            }
        }

        private void SafeGoBack()
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
            else
            {
                LittleWatson.Log("Can not go back");
            }
        }

        private void Load(bool refresh)
        {
            if (lecturesLazyBlock != null)
            {
                lecturesLazyBlock.Cancel();
            }
            var course = App.Crawler.GetCourse(courseId);
            if (!course.Active)
            {
                if (course.HasFinished)
                {
                    messageTextBlock.Text = "Lectures no longer available";
                }
                else
                {
                    messageTextBlock.Text = "Lectures not available yet";
                }
            }
            else
            {
                if (refresh)
                {
                    course = App.Crawler.RefreshCourse(course.Id);
                }
                userInteracted = false;
                lecturesLazyBlock = new LazyBlock<LectureSection[]>(
                    "lectures",
                    "No lectures available. Make sure you have accepted the honor code.",
                    course.LectureSections,
                    a => a.Length == 0,
                    new LazyBlockUI<LectureSection[]>(
                        this,
                        lectureSections =>
                        {
                            pivot.ItemsSource = lectureSections;
                            var lastCompleted = lectureSections.Zip(Enumerable.Range(0, lectureSections.Length), (section, index) => Tuple.Create(index, section))
                                                               .LastOrDefault(tuple => tuple.Item2.Completed);
                            if (!userInteracted && lastCompleted != null && lastCompleted.Item1 < lectureSections.Length - 1)
                            {
                                pivot.SelectedIndex = lastCompleted.Item1 + 1;
                            }
                        },
                        () => pivot.ItemsSource != null,
                        messageTextBlock),
                    false,
                    null,
                    success =>
                    {
                        if (!success)
                        {
                            LittleWatson.Log("Failed to get lectures");
                        }
                    },
                    null);
            }
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);
            if (lecturesLazyBlock != null)
            {
                lecturesLazyBlock.Cancel();
            }
            if (videoLazyBlock != null)
            {
                videoLazyBlock.Cancel();
            }
        }

        private void OnRefreshClick(object sender, EventArgs e)
        {
            LittleWatson.Log("OnRefreshClick");
            Load(true);
        }

        private void OnLectureVideoClick(object sender, RoutedEventArgs e)
        {
            LittleWatson.Log("OnLectureVideoClick");

            if (videoLazyBlock != null)
            {
                LittleWatson.Log("videoLazyBlock is not null");
                return;
            }

            var lecture = (Lecture)((Button)sender).DataContext;
            LittleWatson.Log("Lecture = " + lecture.Title + " [" + lecture.Id + "]");

            if (lecture.DownloadInfo.Downloading)
            {
                LittleWatson.Log("Already downloading");
                return;
            }

            videoLazyBlock = new LazyBlock<string>(
                "video location",
                null,
                lecture.VideoUrl,
                _ => false,
                new LazyBlockUI<string>(
                    this,
                    videoUrl =>
                    {
                        if (lecture.DownloadInfo.Downloaded)
                        {
                            LittleWatson.Log("Launching video");
                            LaunchVideo(lecture.DownloadInfo.VideoLocation);
                        }
                        else
                        {
                            LittleWatson.Log("Queued download");
                            lecture.DownloadInfo.QueueDowload(videoUrl);
                        }
                    },
                    () => false,
                    null),
                false,
                null,
                _ => videoLazyBlock = null,
                null);
        }

        public static void LaunchVideo(Uri videoUrl)
        {
            try
            {
                var launcher = new MediaPlayerLauncher();
                launcher.Location = MediaLocationType.Data;
                launcher.Media = videoUrl;
                launcher.Show();
            }
            catch (Exception ex)
            {
                LittleWatson.ReportException(ex, string.Format("Launching media player for " + videoUrl.OriginalString));
                LittleWatson.CheckForPreviousException(false);
            }
        }

        private void OnLectureNotesClick(object sender, RoutedEventArgs e)
        {
            LittleWatson.Log("OnLectureNotesClick");

            var lecture = (Coursera.Lecture)((Button)sender).DataContext;
            LittleWatson.Log("Lecture = " + lecture.Title + " [" + lecture.Id + "]");

            var task = new WebBrowserTask();
            task.Uri = new Uri(lecture.LectureNotesUrl, UriKind.Absolute);
            task.Show();
        }

        private void OnOpenInBrowserClick(object sender, EventArgs e)
        {
            LittleWatson.Log("OnOpenInBrowserClick");

            var course = App.Crawler.GetCourse(courseId);

            var task = new WebBrowserTask();
            task.Uri = new Uri(course.HomeLink, UriKind.Absolute);
            task.Show();
        }

        private void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            userInteracted = true;
        }

        private void OnScrollViewerManipulationStarted(object sender, System.Windows.Input.ManipulationStartedEventArgs e)
        {
            userInteracted = true;
        }
    }
}