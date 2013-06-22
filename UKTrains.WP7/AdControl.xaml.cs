﻿using System.Windows.Controls;
using Microsoft.Phone.Shell;

#if WP8
using System;
using Windows.ApplicationModel.Store;
#endif

namespace UKTrains
{
    public partial class AdControl : UserControl
    {
        public AdControl()
        {
            InitializeComponent();
        }

        public static void InitAds(Grid grid, IApplicationBar applicationBar)
        {
#if WP8
            var showAds = !CurrentApp.LicenseInformation.ProductLicenses["RemoveAds"].IsActive;
            if (showAds)
            {
                var menuItem = new ApplicationBarMenuItem("Remove ads");
                menuItem.Click += async delegate {
                    await CurrentApp.RequestProductPurchaseAsync("RemoveAds", false);
                    if (CurrentApp.LicenseInformation.ProductLicenses["RemoveAds"].IsActive)
                    {
                        // remove buy option
                        applicationBar.MenuItems.Remove(menuItem);
                        grid.Children.Clear();
                        grid.Width = 0;
                        grid.Height = 0;
                    }
                };
                applicationBar.MenuItems.Add(menuItem);
            }
#else
            var showAds = true;
#endif

            if (showAds)
            {
                var adControl = new AdControl();
                adControl.adControl.AdError += (sender, args) => grid.Dispatcher.BeginInvoke(() => HideAdGrid(grid));
                adControl.adControl.NoAd += (sender, args) => grid.Dispatcher.BeginInvoke(() => HideAdGrid(grid));
                grid.Children.Add(adControl);
                grid.Width = 480;
                grid.Height = 80;
            }
            else
            {
                HideAdGrid(grid);
            }                
        }

        private static void HideAdGrid(Grid grid)
        {
            grid.Children.Clear();
            grid.Width = 0;
            grid.Height = 0;
        }
    }
}