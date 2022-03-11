﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.AutoCAD.Windows;

namespace DriPaletteSet
{
    public class MyPaletteSet : PaletteSet
    {
        private TwinPalette _first = null;
        private SecondPalette _second = null;
        public MyPaletteSet() : base("", "", new Guid("87374E16-C0DB-4F3F-9271-7A71ED921226"))
        {
            MinimumSize = new System.Drawing.Size(300, 300);
            Style = PaletteSetStyles.ShowCloseButton |
                 PaletteSetStyles.ShowAutoHideButton |
                 PaletteSetStyles.ShowTabForSingle;

            _first = new TwinPalette();
            Add("Twin", _first);

            _second = new SecondPalette();
            Add("Enkelt", _second);

            _first.BackColor = System.Drawing.Color.FromArgb(59, 68, 83);
        }
    }
}