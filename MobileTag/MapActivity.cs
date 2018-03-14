﻿using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.Locations;
using Android.Support.V4.App;
using Android.Content.PM;
using MobileTag.Models;
using Microsoft.AspNet.SignalR.Client;
using Android.Support.V4.Widget;
using Android.Support.Design.Widget;
using MobileTag.SharedCode;
using System.Collections.Concurrent;
using Android.Support.V7.App;
using System.Threading;

namespace MobileTag
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme")]
    public class MapActivity : Activity, IOnMapReadyCallback, Android.Locations.ILocationListener, GoogleMap.IOnCameraIdleListener
    {
        private const double CELL_LOAD_RADIUS = .0006;
        private const int ZOOM_LEVEL_LOAD = 15;
        private GoogleMap mMap;
        private float currentZoomLevel = 0.0F;
        private LocationManager locMgr;
        private String provider;
        private bool InitialCameraLocSet = false;
        private LatLng initialCameraLatLng = null;

        private Location lastKnownLocation;
        private TextView lngLatText;
        private Button tagButton;
        private Button locationButton;
        readonly string[] LocationPermissions = { Android.Manifest.Permission.AccessFineLocation, Android.Manifest.Permission.AccessCoarseLocation };
        private const int RequestLocationID = 0;

        private DrawerLayout drawerLayout;
        private NavigationView navigationView;

        ConcurrentDictionary<int, MapOverlay> Overlays = new ConcurrentDictionary<int, MapOverlay>();

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.Map);
         
            var toolbar = FindViewById<Toolbar>(Resource.Id.toolbar);
            SetActionBar(toolbar);
            //toolbar.SetTitleTextColor(ColorCode.GREEN);
            toolbar.SetBackgroundColor(ColorCode.TeamColor(GameModel.Player.Team.ID));
            drawerLayout = FindViewById<DrawerLayout>(Resource.Id.drawer_layout);
            navigationView = FindViewById<NavigationView>(Resource.Id.nav_view);
            lngLatText = FindViewById<TextView>(Resource.Id.textBelowMap);
            tagButton = FindViewById<Button>(Resource.Id.claimButton);
            locationButton = FindViewById<Button>(Resource.Id.clientCameraLocationbtn);
            

            ActionBar.SetHomeAsUpIndicator(Resource.Mipmap.ic_dehaze_white_24dp);
            ActionBar.SetDisplayHomeAsUpEnabled(true);

            drawerLayout.DrawerStateChanged += DrawerLayout_DrawerStateChanged;

            tagButton.Click += TagButton_Click;
            locationButton.Click += LocationButton_Click;

            SetUpMap();
            SetUpCellHub();

            if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted)
            {
                GetLocation();
            }
            else
            {
                RequestPermissions(LocationPermissions, RequestLocationID);
            }



            navigationView.NavigationItemSelected += (sender, e) =>
            {
                e.MenuItem.SetChecked(true);
                
                switch (e.MenuItem.ItemId)
                {
                    case Resource.Id.nav_logout:
                        GameModel.Logout();
                        StartActivity(new Intent(this, typeof(LoginActivity)));
                        break;
                    default:
                        break;
                }
                drawerLayout.CloseDrawers();
            };
        }

        private void DrawerLayout_DrawerStateChanged(object sender, DrawerLayout.DrawerStateChangedEventArgs e)
        {
            TextView usernameHeader = FindViewById<TextView>(Resource.Id.nameTxt);
            usernameHeader.Text = GameModel.Player.Username;
        }

        protected override void OnResume()
        {
            base.OnResume();

            if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted)
            {
                locMgr = GetSystemService(Context.LocationService) as LocationManager;
                locMgr.RequestLocationUpdates(LocationManager.GpsProvider, 10000, 10, this);
                locMgr.RequestLocationUpdates(LocationManager.NetworkProvider, 10000, 10, this);
            }
            if (GameModel.CellHubConnection.State != ConnectionState.Connected && GameModel.CellHubConnection.State != ConnectionState.Connecting)
            {
                GameModel.CellHubConnection.Start().Wait();

                if (initialCameraLatLng != null)
                {
                    // Refresh stale cell data
                    mMap.Clear();
                    Overlays.Clear();

                    DrawCellsInView();
                }
            }
        }

        protected override void OnPause()
        {
            base.OnPause();

            if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted)
            {
                locMgr.RemoveUpdates(this);
            }

            GameModel.CellHubConnection.Stop();
        }
        
        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            drawerLayout.OpenDrawer(Android.Support.V4.View.GravityCompat.Start);

            return base.OnOptionsItemSelected(item);
        }


        // SignalR------------------------------------------------------------------------------------------

        // Set up the cellHub proxy and start the connection. 
        private void SetUpCellHub()
        {
            try
            {
                GameModel.CellHubProxy = GameModel.CellHubConnection.CreateHubProxy("cellHub");

                GameModel.CellHubProxy.On<Cell>("broadcastCell", updatedCell =>
                {
                    // Handle SignalR cell update notification
                    Console.WriteLine("Cell {0} updated!", updatedCell.ID);
                    GameModel.CellsInView[updatedCell.ID] = updatedCell;
                    UpdateOverlay(updatedCell);
                });

                GameModel.CellHubConnection.Start().Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        // -------------------------------------------------------------------------------------------------

        private void SetUpMap()
        {
            if (mMap == null)
            {
                FragmentManager.FindFragmentById<MapFragment>(Resource.Id.map).GetMapAsync(this);
            }
        }

        public void OnMapReady(GoogleMap googleMap)
        {
            // Example code for map style: https://developers.google.com/maps/documentation/android-api/styling
            googleMap.SetMapStyle(MapStyleOptions.LoadRawResourceStyle(this, Resource.Raw.style_json));
            mMap = googleMap;
            mMap.UiSettings.ZoomControlsEnabled = true;
            mMap.SetOnCameraIdleListener(this);
        }

        public void OnCameraIdle()
        {
            currentZoomLevel = mMap.CameraPosition.Zoom;
            LatLng currentCameraLatLng = mMap.CameraPosition.Target;

            if (currentZoomLevel > ZOOM_LEVEL_LOAD)
            {
                if (InitialCameraLocSet == false)
                {
                    initialCameraLatLng = mMap.CameraPosition.Target;
                    InitialCameraLocSet = true;
                    DrawCellsInView();
                }
                else
                {
                    double distanceFromInitialCameraPosition = Math.Sqrt(Math.Pow(currentCameraLatLng.Latitude - initialCameraLatLng.Latitude, 2) + Math.Pow(currentCameraLatLng.Longitude - initialCameraLatLng.Longitude, 2));

                    if (distanceFromInitialCameraPosition > CELL_LOAD_RADIUS)
                    {
                        initialCameraLatLng = mMap.CameraPosition.Target;

                        Toast.MakeText(this, "Loading new cells at Zoom Level: " + currentZoomLevel.ToString(), ToastLength.Long).Show();
                        DrawCellsInView();
                    }
                }
            }
        }

        private void DrawCellsInView()
        {
            ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                if (Overlays.IsEmpty)
                {
                    Overlays = GameModel.LoadProximalCells(initialCameraLatLng);
                }
                else
                {
                    ConcurrentDictionary<int, MapOverlay> temp = GameModel.LoadProximalCells(initialCameraLatLng);

                    foreach (MapOverlay mOverlay in Overlays.Values)
                    {
                        if (mOverlay.IsOnMap)
                        {
                            // Copy the existing overlay into temp
                            temp[mOverlay.CellID] = mOverlay;
                        }
                    }

                    Overlays = temp;
                }

                DrawOverlays();
            }, null);
        }

        public void OnLocationChanged(Location location)
        {
            if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted)
            {
                if (mMap.MyLocationEnabled != true)
                {
                    mMap.MyLocationEnabled = true;
                }

                Double lat, lng;
                lat = location.Latitude;
                lng = location.Longitude;

                lngLatText.Text = "Lat" + lat + " : " + "Long" + lng;
            }
        }

        // Based on example code from https://blog.xamarin.com/requesting-runtime-permissions-in-android-marshmallow/
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            switch (requestCode)
            {
                case RequestLocationID:
                    {
                        if (grantResults[0] == Permission.Granted)
                        {
                            GetLocation();
                        }
                        else
                        {                            
                            StartActivity(new Intent(this, typeof(LoginActivity)));
                        }
                    }

                    break;
            }
        }

        private void GetLocation()
        {
            locMgr = GetSystemService(Context.LocationService) as LocationManager;

            Criteria locationCriteria = new Criteria();
            locationCriteria.Accuracy = Accuracy.Fine;
            locationCriteria.PowerRequirement = Power.Medium;
            provider = locMgr.GetBestProvider(locationCriteria, true);
            lastKnownLocation = locMgr.GetLastKnownLocation(provider);

            if (lastKnownLocation == null)
                System.Diagnostics.Debug.WriteLine("No Location");
        }

        private void CenterMapCameraOnLocation()
        {
            if (mMap.MyLocation != null)
            {
                CameraUpdate mapCameraPos = CameraUpdateFactory.NewLatLngZoom(new LatLng(mMap.MyLocation.Latitude, mMap.MyLocation.Longitude), 17);
                mMap.AnimateCamera(mapCameraPos);
            }
        }

        private void LocationButton_Click(object sender, EventArgs e)
        {
          CenterMapCameraOnLocation();
        }

        private void TagButton_Click(object sender, EventArgs e)
        {
            decimal decLat = (decimal)(mMap.MyLocation.Latitude);
            decimal decLng = (decimal)(mMap.MyLocation.Longitude);
            int playerCellID = GameModel.GetCellID(decLat, decLng);
            Cell cell;

            if (!GameModel.CellsInView.ContainsKey(playerCellID))
            {
                // Generate the new cell and add it to CellsInView
                cell = GameModel.GenerateCell(decLat, decLng);
                GameModel.CellsInView.TryAdd(cell.ID, cell);
            }
            else
            {
                cell = GameModel.CellsInView[playerCellID];
            }

            cell.TeamID = GameModel.Player.Team.ID;
            UpdateOverlay(cell);

            try
            {
                cell.Tag();
            }
            catch (AggregateException exc)
            {
                foreach (Exception ie in exc.InnerExceptions)
                    Console.WriteLine(ie.ToString());
            }
            catch (Exception o)
            {
                Console.WriteLine(o.ToString());
            }
        }

        public void UpdateOverlay(Cell updatedCell)
        {
            if (!Overlays.ContainsKey(updatedCell.ID))
            {
                MapOverlay mapOverlay = new MapOverlay(updatedCell);

                RunOnUiThread(() =>
                {
                    mapOverlay.Polygon = mMap.AddPolygon(mapOverlay.PolygonOptions);
                });

                Overlays.TryAdd(updatedCell.ID, mapOverlay);
            }
            else
            {
                RunOnUiThread(() =>
                {
                    Overlays[updatedCell.ID].SetColor(ColorCode.TeamColor(updatedCell.TeamID));
                });
            }
        }

        private void DrawOverlays()
        {
            RunOnUiThread(() =>
            {
                if (mMap.MyLocation != null)
                {
                    double lat = mMap.MyLocation.Latitude;
                    double lng = mMap.MyLocation.Longitude;
                    int playerCellID = GameModel.GetCellID((decimal)lat, (decimal)lng);
                    LatLng latlng = GameModel.GetLatLng(playerCellID);

                    lngLatText.Text = "Lat " + lat + " : " + "Long " + lng;
                }
            });

            foreach (MapOverlay overlay in Overlays.Values)
            {
                if (!overlay.IsOnMap)
                {
                    RunOnUiThread(() => overlay.Polygon = mMap.AddPolygon(overlay.PolygonOptions));
                }
            }
        }

        public void OnProviderDisabled(string provider)
        {
            //throw new NotImplementedException();
        }

        public void OnProviderEnabled(string provider)
        {
            //throw new NotImplementedException();
        }

        public void OnStatusChanged(string provider, [GeneratedEnum] Availability status, Bundle extras)
        {
            //throw new NotImplementedException();
        }
    }
}