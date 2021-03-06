﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace MobileTag.Models
{
    public class Player
    {
        public string Username { get; set; }
        public int ID { get; }
        public Team Team { get; set; }
        public int CurrentCellID { get; set; }

        public Player(int id, string username, Team team, int currentCellID)
        {
            ID = id;
            Team = team;
            CurrentCellID = currentCellID;
            Username = username;
        }

        public Player(int id, string username, Team team, decimal lat, decimal lng)
        {
            ID = id;
            Team = team;
            CurrentCellID = Cell.FindID(lat, lng);
            Username = username;
        }
    }
}