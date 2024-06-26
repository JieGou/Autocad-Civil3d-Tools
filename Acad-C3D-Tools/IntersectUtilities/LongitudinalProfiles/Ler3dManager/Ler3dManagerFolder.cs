﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IntersectUtilities.UtilsCommon;
using NetTopologySuite.Geometries;
using Autodesk.Civil.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using System.Text.RegularExpressions;

namespace IntersectUtilities.LongitudinalProfiles
{
    public class Ler3dManagerFolder : Ler3dManagerBase
    {
        private Dictionary<string, Database> storage = new Dictionary<string, Database>();
        private Dictionary<string, Transaction> trans = new Dictionary<string, Transaction>();
        private Dictionary<string, Polygon> areas = new Dictionary<string, Polygon>();
        public override void Load(string path)
        {
            var files = Directory.EnumerateFiles(path, "*_3DLER.dwg", SearchOption.TopDirectoryOnly);

            if (files.Count() == 0)
                throw new Exception($"No files with search mask \"*_3DLER.dwg\" found in {path}!");

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var db = new Database(false, true);
                db.ReadDwgFile(file, FileOpenMode.OpenForReadAndReadShare, true, "");
                storage.Add(name, db);

                Transaction tx = db.TransactionManager.StartTransaction();
                MPolygon mpg = db.ListOfType<MPolygon>(tx).FirstOrDefault();

                if (mpg == null)
                    throw new Exception($"No MPolygon found in {file}!");

                trans.Add(name, tx);
                areas.Add(name, NTS.NTSConversion.ConvertMPolygonToNTSPolygon(mpg));
            }
            if (!IsLoadValid()) throw new Exception("Ler3d load failed!: \n" + path);
        }
        protected override bool IsLoadValid() =>
            storage != null &&
            storage.Count > 0 &&
            !storage.Values.Any(x => x == null);
        public override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Transaction item in trans?.Values)
                {
                    if (item != null)
                    {
                        item.Abort();
                        item.Dispose();
                    }
                }

                foreach (var db in storage?.Values)
                {
                    while (db?.TransactionManager?.TopTransaction != null)
                    {
                        db?.TransactionManager?.TopTransaction?.Abort();
                        db?.TransactionManager?.TopTransaction?.Dispose();
                    }
                    if (db?.TransactionManager?.TopTransaction != null)
                        throw new Exception("Cannot dispose before transaction is closed!");
                    db?.Dispose();
                }
            }
            base.Dispose(disposing);
        }
        public override HashSet<Entity> GetIntersectingEntities(Alignment al)
        {
            HashSet<Entity> result = new HashSet<Entity>();

            var pline = al.GetPolyline().Go<Polyline>(
                al.Database.TransactionManager.TopTransaction);
            var line = NTS.NTSConversion.ConvertPlineToNTSLineString(pline);
            pline.UpgradeOpen();
            pline.Erase(true);
            Plane plane = new Plane();

            foreach (var entry in areas)
            {
                if (entry.Value.Intersects(line))
                {
                    var db = storage[entry.Key];
                    //var tx = db.TransactionManager.TopTransaction;
                    var tx = trans[entry.Key];
                    if (tx == null) db.TransactionManager.StartTransaction();
                    var plines = db.ListOfType<Polyline3d>(tx);
                    foreach (var pl in plines)
                    {
                        string type = UtilsDataTables.ReadStringParameterFromDataTable(
                            pl.Layer, CsvData.Get("krydsninger"), "Type", 0);
                        if (type == "IGNORE") continue;

                        using (Point3dCollection p3dcol = new Point3dCollection())
                        {
                            al.IntersectWith(
                                pl,
                                Autodesk.AutoCAD.DatabaseServices.Intersect.OnBothOperands,
                                plane, p3dcol, new IntPtr(0), new IntPtr(0));

                            if (p3dcol.Count > 0) result.Add(pl);
                        }
                    }
                }
            }

            return result;
        }
        public override string GetHandle(Entity ent)
        {
            Database db = ent.Database;
            string filename = Path.GetFileName(db.Filename);
            foreach (var item in storage)
            {
                if (item.Value.Filename == db.Filename)
                    return filename + ":" + ent.Handle.ToString();
            }
            throw new Exception($"Entitys' {ent.Handle}\nDB {db.Filename}" +
                $"\nnot found in GetHandle!");
        }
        public override bool IsPointWithinPolygon(Entity ent, Point3d p3d)
        {
            Database db = ent.Database;
            foreach (var item in storage)
            {
                if (item.Value.Filename == db.Filename)
                {
                    string area = item.Key;
                    var polygon = areas[area];
                    return polygon.Contains(
                        new NetTopologySuite.Geometries.Point(p3d.X, p3d.Y));
                }
            }
            throw new Exception($"Entitys' {ent.Handle}\nDB {db.Filename}" +
                $"\nnot found in IsPointWithinPolygon!");
        }
        private Regex rgx = new Regex(@"^(?<PROJECT>[\w.]+):(?<ETAPE>[\w.]+):(?<AREA>[\w.]+?)(.dwg)?:(?<HANDLE>\w+)");
        public override Entity GetEntityByHandle(string handle)
        {
            if (rgx.IsMatch(handle))
            {
                var match = rgx.Match(handle);
                string area = match.Groups["AREA"].Value;
                string hndl = match.Groups["HANDLE"].Value;
                if (!storage.ContainsKey(area)) throw new Exception($"Area {area} not found in Ler3dManager!");
                var db = storage[area];
                return db.Go<Entity>(hndl);
            }
            else return null;
        }
    }
}
