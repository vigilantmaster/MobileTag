﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System.Drawing;
using Microsoft.AspNet.SignalR.Client;
using Android.Gms.Maps.Model;
using MobileTag.SharedCode;

namespace MobileTag.Models
{
    public class GameModel : Application
    {
        // IMPORTANT: corresponds to Frontier's specifications.
        // Do not change this unless you first recreate the Frontier's cell database using your new specifications.
        public const decimal frontierInterval = .0005m;
        public const decimal frontierLowerLeftLat = 45.0m;
        public const decimal frontierLowerLeftLong = -123.4m;
        public const decimal frontierUpperRightLat = 45.5m;
        public const decimal frontierUpperRightLong = -122.4m;
        private const int viewRadius = 2;

        public static decimal FrontierInterval => frontierInterval;

        public static ConcurrentDictionary<int, Cell> CellsInView = new ConcurrentDictionary<int, Cell>();
        public static Player Player { get; set; }

        // Calculated constants
        public const decimal GridHeight = ((frontierUpperRightLat - frontierLowerLeftLat) / frontierInterval);
        public const decimal GridWidth = ((frontierUpperRightLong - frontierLowerLeftLong) / frontierInterval);

        // SignalR
        public static HubConnection CellHubConnection = new HubConnection("https://mobiletag.azurewebsites.net/");
        public static IHubProxy CellHubProxy;

        async static public void SubscribeToUpdates(HashSet<int> cellIDs)
        {
            try
            {
                await GameModel.CellHubProxy.Invoke("SubscribeToCellUpdates", cellIDs);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        async static public void SubscribeToUpdates(ConcurrentDictionary<int, Cell> cellsInView)
        {
            HashSet<int> cellIDs = new HashSet<int>();

            foreach(Cell cell in cellsInView.Values)
            {
                cellIDs.Add(cell.ID);
            }

            try
            {
                await GameModel.CellHubProxy.Invoke("SubscribeToCellUpdates",cellIDs);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        async static public void SubscribeToUpdates()
        {
            HashSet<int> cellIDs = new HashSet<int>();

            foreach (Cell cell in CellsInView.Values)
            {
                cellIDs.Add(cell.ID);
            }

            try
            {
                await GameModel.CellHubProxy.Invoke("SubscribeToCellUpdates", cellIDs);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        //async static public void UnsubscribeFromUpdates(int cellID)
        //{
        //    try
        //    {
        //        await GameModel.CellHubProxy.Invoke("UnsubscribeFromCellUpdates", cellID);
        //    }
        //    catch (Exception e)
        //    {
        //        Console.WriteLine(e.ToString());
        //    }
        //}

        public static int GetCellID(decimal lat, decimal lng)
        {
            decimal nearestLatInterval, nearestLongInterval;

            nearestLatInterval = (Math.Floor((lat - frontierLowerLeftLat) / frontierInterval) * frontierInterval);
            nearestLongInterval = (Math.Floor((lng - frontierLowerLeftLong) / frontierInterval) * frontierInterval);

            int id = Convert.ToInt32((nearestLongInterval / frontierInterval) + (nearestLatInterval / frontierInterval)
                * ((frontierUpperRightLong - frontierLowerLeftLong) / frontierInterval));

            return id;
        }

        public static LatLng GetLatLng(int cellID)
        {
            decimal lat = frontierLowerLeftLat + (cellID / GridWidth * frontierInterval);
            decimal lng = frontierLowerLeftLong + (cellID % GridWidth * frontierInterval);

            LatLng latLng = new LatLng((double)lat, (double)lng);

            return latLng;
        }

        public static ConcurrentDictionary<int, MapOverlay> LoadProximalCells(LatLng playerLatLng)
        {
            CellsInView = RetrieveProximalCells(playerLatLng);

            var Overlays = new ConcurrentDictionary<int, MapOverlay>();

            foreach (Cell cell in CellsInView.Values)
            {
                Overlays.TryAdd(cell.ID, new MapOverlay(cell));
            }

            SubscribeToUpdates();

            return Overlays;
        }

        private static ConcurrentDictionary<int, Cell> RetrieveProximalCells(LatLng playerLatLng)
        {
            int playerCellID = GetCellID((decimal)playerLatLng.Latitude, (decimal)playerLatLng.Longitude);
            ConcurrentDictionary<int, Cell> frontierDict = CellsInView;

            for (int row = -viewRadius; row <= viewRadius; row++)
            {
                for (int col = -viewRadius; col <= viewRadius; col++)
                {
                    int cellID = (int)(playerCellID + (row * GridWidth) + col);
                    Cell cell = Database.GetCell(cellID);

                    if (cell.ID == -1)
                    {
                        // Cell does not exist in DB

                        decimal cellLat = Math.Floor((decimal)playerLatLng.Latitude / frontierInterval) * frontierInterval + (row * frontierInterval);
                        decimal cellLng = Math.Floor((decimal)playerLatLng.Longitude / frontierInterval) * frontierInterval + (col * frontierInterval);
                        cell = new Cell(cellLat, cellLng);
                    }

                    if (!CellsInView.Keys.Contains(cell.ID))
                    {
                        frontierDict.TryAdd(cell.ID, cell);
                    }
                }
            }

            return frontierDict;
        }
    }
}