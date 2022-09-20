﻿using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Civil.DataShortcuts;
using Autodesk.Gis.Map;
using Autodesk.Gis.Map.ObjectData;
using Autodesk.Gis.Map.Utilities;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Data;
using MoreLinq;
using GroupByCluster;
using IntersectUtilities.UtilsCommon;
using Dreambuild.AutoCAD;

using static IntersectUtilities.Enums;
using static IntersectUtilities.HelperMethods;
using static IntersectUtilities.Utils;
using static IntersectUtilities.PipeSchedule;

using static IntersectUtilities.UtilsCommon.UtilsDataTables;
using static IntersectUtilities.UtilsCommon.UtilsODData;

using BlockReference = Autodesk.AutoCAD.DatabaseServices.BlockReference;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;
using DataType = Autodesk.Gis.Map.Constants.DataType;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectIdCollection = Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection;
using Oid = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Label = Autodesk.Civil.DatabaseServices.Label;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using System.Windows.Documents;

namespace IntersectUtilities
{
    public partial class Intersect
    {
        [CommandMethod("finalizesheets")]
        public void finalizesheets()
        {
            DocumentCollection docCol = Application.DocumentManager;
            Database localDb = docCol.MdiActiveDocument.Database;
            Editor ed = docCol.MdiActiveDocument.Editor;
            Document doc = docCol.MdiActiveDocument;
            CivilDocument civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

            DataReferencesOptions dro = new DataReferencesOptions();

            //Create crossing points first
            createlerdatapssmethod(dro);
            //Populateprofileviews with crossing data
            populateprofiles();

            using (Transaction tx = localDb.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable bt = tx.GetObject(localDb.BlockTableId, OpenMode.ForWrite) as BlockTable;

                    #region Stylize Profile Views
                    HashSet<ProfileView> pvs = localDb.HashSetOfType<ProfileView>(tx);

                    Oid pvStyleId = Oid.Null;
                    try
                    {
                        pvStyleId = civilDoc.Styles.ProfileViewStyles["PROFILE VIEW L TO R 1:250:100"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nProfile view style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    HashSet<Alignment> alss = localDb.HashSetOfType<Alignment>(tx);
                    foreach (Alignment al in alss)
                    {
                        al.CheckOrOpenForWrite();
                        al.StyleId = civilDoc.Styles.AlignmentStyles["FJV TRACÉ SHOW"];
                        al.ImportLabelSet("STD 20-5");
                        al.DowngradeOpen();
                    }

                    foreach (ProfileView pv in pvs)
                    {
                        pv.CheckOrOpenForWrite();
                        pv.StyleId = pvStyleId;

                        Oid alId = pv.AlignmentId;
                        Alignment al = alId.Go<Alignment>(tx);

                        ObjectIdCollection psIds = al.GetProfileIds();
                        HashSet<Profile> ps = new HashSet<Profile>();
                        foreach (Oid oid in psIds) ps.Add(oid.Go<Profile>(tx));

                        Profile surfaceProfile = ps.Where(x => x.Name.Contains("surface")).FirstOrDefault();
                        Oid surfaceProfileId = Oid.Null;
                        if (surfaceProfile != null) surfaceProfileId = surfaceProfile.ObjectId;
                        else ed.WriteMessage("\nSurface profile not found!");

                        Profile topProfile = ps.Where(x => x.Name.Contains("TOP")).FirstOrDefault();
                        Oid topProfileId = Oid.Null;
                        if (topProfile != null) topProfileId = topProfile.ObjectId;
                        else ed.WriteMessage("\nTop profile not found!");

                        //this doesn't quite work
                        Oid pvbsId = civilDoc.Styles.ProfileViewBandSetStyles["EG-FG Elevations and Stations"];
                        ProfileViewBandSet pvbs = pv.Bands;
                        pvbs.ImportBandSetStyle(pvbsId);

                        //try this
                        Oid pvBSId1 = civilDoc.Styles.BandStyles.ProfileViewProfileDataBandStyles["Elevations and Stations"];
                        Oid pvBSId2 = civilDoc.Styles.BandStyles.ProfileViewProfileDataBandStyles["TitleBuffer"];
                        ProfileViewBandItemCollection pvic = new ProfileViewBandItemCollection(pv.Id, BandLocationType.Bottom);
                        pvic.Add(pvBSId1);
                        pvic.Add(pvBSId2);
                        pvbs.SetBottomBandItems(pvic);

                        ProfileViewBandItemCollection pbic = pvbs.GetBottomBandItems();
                        for (int i = 0; i < pbic.Count; i++)
                        {
                            ProfileViewBandItem pvbi = pbic[i];
                            if (i == 0) pvbi.Gap = 0;
                            else if (i == 1) pvbi.Gap = 0.016;
                            if (surfaceProfileId != Oid.Null) pvbi.Profile1Id = surfaceProfileId;
                            if (topProfileId != Oid.Null) pvbi.Profile2Id = topProfileId;
                            pvbi.LabelAtStartStation = true;
                            pvbi.LabelAtEndStation = true;
                        }
                        pvbs.SetBottomBandItems(pbic);

                        #region Scale LER block
                        if (bt.Has(pv.Name))
                        {
                            BlockTableRecord btr = tx.GetObject(bt[pv.Name], OpenMode.ForRead)
                                as BlockTableRecord;
                            ObjectIdCollection brefIds = btr.GetBlockReferenceIds(false, true);

                            foreach (Oid oid in brefIds)
                            {
                                BlockReference bref = oid.Go<BlockReference>(tx, OpenMode.ForWrite);
                                bref.ScaleFactors = new Scale3d(1, 2.5, 1);
                            }

                        }
                        #endregion
                    }
                    #endregion

                    #region ProfileStyles
                    Oid pPipeStyleKantId = Oid.Null;
                    try
                    {
                        pPipeStyleKantId = civilDoc.Styles.ProfileStyles["PROFIL STYLE MGO KANT"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nPROFIL STYLE MGO KANT style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid pPipeStyleMidtId = Oid.Null;
                    try
                    {
                        pPipeStyleMidtId = civilDoc.Styles.ProfileStyles["PROFIL STYLE MGO MIDT"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nPROFIL STYLE MGO MIDT style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid pTerStyleId = Oid.Null;
                    try
                    {
                        pTerStyleId = civilDoc.Styles.ProfileStyles["Terræn"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nTerræn style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid alStyleId = Oid.Null;
                    try
                    {
                        alStyleId = civilDoc.Styles.AlignmentStyles["FJV TRACÉ SHOW"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nFJV TRACÈ SHOW style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid alLabelSetStyleId = Oid.Null;
                    try
                    {
                        alLabelSetStyleId = civilDoc.Styles.LabelSetStyles.AlignmentLabelSetStyles["STD 20-5"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nSTD 20-5 style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid crestCurveLabelId = Oid.Null;
                    try
                    {
                        crestCurveLabelId = civilDoc.Styles.LabelStyles.ProfileLabelStyles.CurveLabelStyles["Radius Crest"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nRADIUS CREST style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid sagCurveLabelId = Oid.Null;
                    try
                    {
                        sagCurveLabelId = civilDoc.Styles.LabelStyles.ProfileLabelStyles.CurveLabelStyles["Radius Sag"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nRADIUS SAG style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    HashSet<Alignment> als = localDb.HashSetOfType<Alignment>(tx);
                    foreach (Alignment al in als)
                    {
                        al.CheckOrOpenForWrite();
                        al.StyleId = alStyleId;
                        al.ImportLabelSet(alLabelSetStyleId);

                        ObjectIdCollection pIds = al.GetProfileIds();
                        foreach (Oid oid in pIds)
                        {
                            Profile p = oid.Go<Profile>(tx);
                            if (p.Name == $"{al.Name}_surface_P")
                            {
                                p.CheckOrOpenForWrite();
                                p.StyleId = pTerStyleId;
                            }
                            else
                            {
                                p.CheckOrOpenForWrite();
                                p.StyleId = pPipeStyleKantId;

                                if (p.Name.Contains("MIDT"))
                                {
                                    p.StyleId = pPipeStyleMidtId;

                                    foreach (ProfileView pv in pvs)
                                    {
                                        pv.CheckOrOpenForWrite();
                                        ProfileCrestCurveLabelGroup.Create(pv.ObjectId, p.ObjectId, crestCurveLabelId);
                                        ProfileSagCurveLabelGroup.Create(pv.ObjectId, p.ObjectId, sagCurveLabelId);
                                    }
                                }
                            }
                        }
                    }
                    #endregion
                }
                catch (System.Exception ex)
                {
                    tx.Abort();
                    ed.WriteMessage("\n" + ex.ToString());
                    return;
                }
                tx.Commit();
            }

            //Create detailing blocks on top of exaggerated views
            createdetailingmethod(dro, localDb);
            //Auto stagger all labels to right
            staggerlabelsall();
            //Draw rectangles representing viewports around longitudinal profiles
            //Can be used to check if labels are inside
            drawviewportrectangles();
            //Colorize layer as per krydsninger table
            colorizealllerlayersmethod();
        }

        [CommandMethod("finalizesheetsauto")]
        public void finalizesheetsauto()
        {
            DocumentCollection docCol = Application.DocumentManager;
            Database localDb = docCol.MdiActiveDocument.Database;
            Editor ed = docCol.MdiActiveDocument.Editor;
            Document doc = docCol.MdiActiveDocument;
            CivilDocument civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

            //Create crossing points first
            DataReferencesOptions dro = new DataReferencesOptions();
            createlerdatapssmethod(dro);

            //Populateprofileviews with crossing data
            populateprofiles();

            using (Transaction tx = localDb.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable bt = tx.GetObject(localDb.BlockTableId, OpenMode.ForWrite) as BlockTable;

                    #region Stylize Profile Views
                    HashSet<ProfileView> pvs = localDb.HashSetOfType<ProfileView>(tx);

                    Oid pvStyleId = Oid.Null;
                    try
                    {
                        pvStyleId = civilDoc.Styles.ProfileViewStyles["PROFILE VIEW L TO R 1:250:100"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nProfile view style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    HashSet<Alignment> alss = localDb.HashSetOfType<Alignment>(tx);
                    foreach (Alignment al in alss)
                    {
                        al.CheckOrOpenForWrite();
                        al.StyleId = civilDoc.Styles.AlignmentStyles["FJV TRACÉ SHOW"];
                        al.ImportLabelSet("STD 20-5");
                        al.DowngradeOpen();
                    }

                    foreach (ProfileView pv in pvs)
                    {
                        pv.CheckOrOpenForWrite();
                        pv.StyleId = pvStyleId;

                        Oid alId = pv.AlignmentId;
                        Alignment al = alId.Go<Alignment>(tx);

                        ObjectIdCollection psIds = al.GetProfileIds();
                        HashSet<Profile> ps = new HashSet<Profile>();
                        foreach (Oid oid in psIds) ps.Add(oid.Go<Profile>(tx));

                        Profile surfaceProfile = ps.Where(x => x.Name.Contains("surface")).FirstOrDefault();
                        Oid surfaceProfileId = Oid.Null;
                        if (surfaceProfile != null) surfaceProfileId = surfaceProfile.ObjectId;
                        else ed.WriteMessage("\nSurface profile not found!");

                        Profile topProfile = ps.Where(x => x.Name.Contains("TOP")).FirstOrDefault();
                        Oid topProfileId = Oid.Null;
                        if (topProfile != null) topProfileId = topProfile.ObjectId;
                        else ed.WriteMessage("\nTop profile not found!");

                        //this doesn't quite work
                        Oid pvbsId = civilDoc.Styles.ProfileViewBandSetStyles["EG-FG Elevations and Stations"];
                        ProfileViewBandSet pvbs = pv.Bands;
                        pvbs.ImportBandSetStyle(pvbsId);

                        //try this
                        Oid pvBSId1 = civilDoc.Styles.BandStyles.ProfileViewProfileDataBandStyles["Elevations and Stations"];
                        Oid pvBSId2 = civilDoc.Styles.BandStyles.ProfileViewProfileDataBandStyles["TitleBuffer"];
                        ProfileViewBandItemCollection pvic = new ProfileViewBandItemCollection(pv.Id, BandLocationType.Bottom);
                        pvic.Add(pvBSId1);
                        pvic.Add(pvBSId2);
                        pvbs.SetBottomBandItems(pvic);

                        ProfileViewBandItemCollection pbic = pvbs.GetBottomBandItems();
                        for (int i = 0; i < pbic.Count; i++)
                        {
                            ProfileViewBandItem pvbi = pbic[i];
                            if (i == 0) pvbi.Gap = 0;
                            else if (i == 1) pvbi.Gap = 0.016;
                            if (surfaceProfileId != Oid.Null) pvbi.Profile1Id = surfaceProfileId;
                            if (topProfileId != Oid.Null) pvbi.Profile2Id = topProfileId;
                            pvbi.LabelAtStartStation = true;
                            pvbi.LabelAtEndStation = true;
                        }
                        pvbs.SetBottomBandItems(pbic);

                        #region Scale LER block
                        if (bt.Has(pv.Name))
                        {
                            BlockTableRecord btr = tx.GetObject(bt[pv.Name], OpenMode.ForRead)
                                as BlockTableRecord;
                            ObjectIdCollection brefIds = btr.GetBlockReferenceIds(false, true);

                            foreach (Oid oid in brefIds)
                            {
                                BlockReference bref = oid.Go<BlockReference>(tx, OpenMode.ForWrite);
                                bref.ScaleFactors = new Scale3d(1, 2.5, 1);
                            }

                        }
                        #endregion
                    }
                    #endregion

                    #region ProfileStyles
                    Oid pPipeStyleKantId = Oid.Null;
                    try
                    {
                        pPipeStyleKantId = civilDoc.Styles.ProfileStyles["PROFIL STYLE MGO KANT"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nPROFIL STYLE MGO KANT style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid pPipeStyleMidtId = Oid.Null;
                    try
                    {
                        pPipeStyleMidtId = civilDoc.Styles.ProfileStyles["PROFIL STYLE MGO MIDT"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nPROFIL STYLE MGO MIDT style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid pTerStyleId = Oid.Null;
                    try
                    {
                        pTerStyleId = civilDoc.Styles.ProfileStyles["Terræn"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nTerræn style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid alStyleId = Oid.Null;
                    try
                    {
                        alStyleId = civilDoc.Styles.AlignmentStyles["FJV TRACÉ SHOW"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nFJV TRACÈ SHOW style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid alLabelSetStyleId = Oid.Null;
                    try
                    {
                        alLabelSetStyleId = civilDoc.Styles.LabelSetStyles.AlignmentLabelSetStyles["STD 20-5"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nSTD 20-5 style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid crestCurveLabelId = Oid.Null;
                    try
                    {
                        crestCurveLabelId = civilDoc.Styles.LabelStyles.ProfileLabelStyles.CurveLabelStyles["Radius Crest"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nRADIUS CREST style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    Oid sagCurveLabelId = Oid.Null;
                    try
                    {
                        sagCurveLabelId = civilDoc.Styles.LabelStyles.ProfileLabelStyles.CurveLabelStyles["Radius Sag"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nRADIUS SAG style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    HashSet<Alignment> als = localDb.HashSetOfType<Alignment>(tx);
                    foreach (Alignment al in als)
                    {
                        al.CheckOrOpenForWrite();
                        al.StyleId = alStyleId;
                        al.ImportLabelSet(alLabelSetStyleId);

                        ObjectIdCollection pIds = al.GetProfileIds();
                        foreach (Oid oid in pIds)
                        {
                            Profile p = oid.Go<Profile>(tx);
                            if (p.Name == $"{al.Name}_surface_P")
                            {
                                p.CheckOrOpenForWrite();
                                p.StyleId = pTerStyleId;
                            }
                            else
                            {
                                p.CheckOrOpenForWrite();
                                p.StyleId = pPipeStyleKantId;

                                if (p.Name.Contains("MIDT"))
                                {
                                    p.StyleId = pPipeStyleMidtId;

                                    foreach (ProfileView pv in pvs)
                                    {
                                        pv.CheckOrOpenForWrite();
                                        ProfileCrestCurveLabelGroup.Create(pv.ObjectId, p.ObjectId, crestCurveLabelId);
                                        ProfileSagCurveLabelGroup.Create(pv.ObjectId, p.ObjectId, sagCurveLabelId);
                                    }
                                }
                            }
                        }
                    }
                    #endregion
                }
                catch (System.Exception ex)
                {
                    tx.Abort();
                    ed.WriteMessage("\n" + ex.ToString());
                    return;
                }
                tx.Commit();
            }

            //Create detailing blocks on top of exaggerated views
            createdetailingmethod(dro, localDb);
            //Auto stagger all labels to right
            staggerlabelsall();
            //Draw rectangles representing viewports around longitudinal profiles
            //Can be used to check if labels are inside
            drawviewportrectangles();
            //Colorize layer as per krydsninger table
            colorizealllerlayersmethod();
        }

        [CommandMethod("resetprofileviews")]
        public void resetprofileviews()
        {
            DocumentCollection docCol = Application.DocumentManager;
            Database localDb = docCol.MdiActiveDocument.Database;
            Editor ed = docCol.MdiActiveDocument.Editor;
            Document doc = docCol.MdiActiveDocument;
            CivilDocument civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

            deletedetailingmethod(localDb);

            using (Transaction tx = localDb.TransactionManager.StartTransaction())
            {
                try
                {
                    #region Delete cogo points
                    CogoPointCollection cogoPoints = civilDoc.CogoPoints;
                    ObjectIdCollection cpIds = new ObjectIdCollection();
                    foreach (Oid oid in cogoPoints) cpIds.Add(oid);
                    foreach (Oid oid in cpIds) cogoPoints.Remove(oid);
                    #endregion

                    #region Stylize Profile Views
                    HashSet<ProfileView> pvs = localDb.HashSetOfType<ProfileView>(tx);

                    Oid pvStyleId = Oid.Null;
                    try
                    {
                        pvStyleId = civilDoc.Styles.ProfileViewStyles["PROFILE VIEW L TO R NO SCALE"];
                    }
                    catch (System.Exception)
                    {
                        ed.WriteMessage($"\nProfile view style missing! Run IMPORTLABELSTYLES.");
                        tx.Abort();
                        return;
                    }

                    foreach (ProfileView pv in pvs)
                    {
                        pv.CheckOrOpenForWrite();
                        pv.StyleId = pvStyleId;

                        var brs = localDb.HashSetOfType<BlockReference>(tx);
                        foreach (BlockReference br in brs)
                        {
                            if (br.Name == pv.Name)
                            {
                                br.CheckOrOpenForWrite();
                                br.Erase(true);
                            }
                        }
                    }
                    #endregion
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\n" + ex.Message);
                    tx.Abort();
                    return;
                }
                tx.Commit();
            }
        }

        [CommandMethod("FINALIZEVIEWFRAMES")]
        [CommandMethod("FVF")]
        public void finalizeviewframes()
        {
            DocumentCollection docCol = Application.DocumentManager;
            Database localDb = docCol.MdiActiveDocument.Database;
            Editor editor = docCol.MdiActiveDocument.Editor;
            Document doc = docCol.MdiActiveDocument;
            CivilDocument civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

            try
            {
                #region Operation

                string path = string.Empty;
                OpenFileDialog dialog = new OpenFileDialog()
                {
                    Title = "Choose txt file:",
                    DefaultExt = "txt",
                    Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*",
                    FilterIndex = 0
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    path = dialog.FileName;
                }
                else return;

                List<string> fileList;
                fileList = File.ReadAllLines(path).ToList();
                path = Path.GetDirectoryName(path) + "\\";

                foreach (string name in fileList)
                {
                    prdDbg(name);
                    string fileName = path + name;

                    using (Database extDb = new Database(false, true))
                    {
                        extDb.ReadDwgFile(fileName, System.IO.FileShare.ReadWrite, false, "");

                        using (Transaction extTx = extDb.TransactionManager.StartTransaction())
                        {
                            #region Change Alignment style
                            CivilDocument extCDoc = CivilDocument.GetCivilDocument(extDb);

                            HashSet<Alignment> als = extDb.HashSetOfType<Alignment>(extTx);

                            foreach (Alignment al in als)
                            {
                                al.CheckOrOpenForWrite();
                                al.StyleId = extCDoc.Styles.AlignmentStyles["FJV TRACE NO SHOW"];
                                Oid labelSetOid = extCDoc.Styles.LabelSetStyles.AlignmentLabelSetStyles["_No Labels"];
                                al.ImportLabelSet(labelSetOid);
                            }
                            #endregion

                            extTx.Commit();
                        }
                        extDb.SaveAs(extDb.Filename, true, DwgVersion.Current, null);
                    }
                    System.Windows.Forms.Application.DoEvents();
                }
                #endregion
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n" + ex.Message);
                return;
            }
        }

        [CommandMethod("DETACHATTACHDWG")]
        public void detachattachdwg()
        {
            DocumentCollection docCol = Application.DocumentManager;
            Database localDb = docCol.MdiActiveDocument.Database;
            Editor editor = docCol.MdiActiveDocument.Editor;
            Document doc = docCol.MdiActiveDocument;
            CivilDocument civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

            try
            {
                #region Operation

                //************************************
                string xrefName = "Fremtidig fjernvarme";
                string xrefPath = @"X:\037-1178 - Gladsaxe udbygning - Dokumenter\01 Intern\02 Tegninger\" +
                                  @"01 Autocad - xxx\Etape 1.2\Fremtidig fjernvarme.dwg";
                //************************************

                string path = string.Empty;
                OpenFileDialog dialog = new OpenFileDialog()
                {
                    Title = "Choose txt file:",
                    DefaultExt = "txt",
                    Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*",
                    FilterIndex = 0
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    path = dialog.FileName;
                }
                else return;

                List<string> fileList;
                fileList = File.ReadAllLines(path).ToList();
                path = Path.GetDirectoryName(path) + "\\";

                foreach (string name in fileList)
                {
                    prdDbg(name);
                    string fileName = path + name;

                    using (Database extDb = new Database(false, true))
                    {
                        extDb.ReadDwgFile(fileName, System.IO.FileShare.ReadWrite, false, "");

                        #region Detach Fremtidig fjernvarme
                        using (Transaction extTx = extDb.TransactionManager.StartTransaction())
                        {
                            try
                            {
                                BlockTable bt = extTx.GetObject(extDb.BlockTableId, OpenMode.ForRead) as BlockTable;

                                foreach (Oid oid in bt)
                                {
                                    BlockTableRecord btr = extTx.GetObject(oid, OpenMode.ForWrite) as BlockTableRecord;
                                    //if (btr.Name.Contains("_alignment"))
                                    if (btr.Name == xrefName && btr.IsFromExternalReference)
                                    {
                                        extDb.DetachXref(btr.ObjectId);
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {
                                prdDbg(ex.ToString());
                                extTx.Abort();
                                throw;
                            }

                            extTx.Commit();
                        }
                        #endregion

                        #region Attach Fremtidig fjernvarme and change draw order
                        using (Transaction extTx = extDb.TransactionManager.StartTransaction())
                        {
                            try
                            {
                                BlockTable bt = extTx.GetObject(extDb.BlockTableId, OpenMode.ForRead) as BlockTable;

                                Oid xrefId = extDb.AttachXref(xrefPath, xrefName);
                                if (xrefId == Oid.Null) throw new System.Exception("Creating xref failed!");

                                Point3d insPt = new Point3d(0, 0, 0);
                                using (BlockReference br = new BlockReference(insPt, xrefId))
                                {
                                    BlockTableRecord modelSpace = extDb.GetModelspaceForWrite();
                                    modelSpace.AppendEntity(br);
                                    extTx.AddNewlyCreatedDBObject(br, true);

                                    br.Layer = "XREF-FJV_FREMTID";

                                    DrawOrderTable dot = modelSpace.DrawOrderTableId.Go<DrawOrderTable>(extTx);
                                    dot.CheckOrOpenForWrite();

                                    Alignment al = extDb.ListOfType<Alignment>(extTx).FirstOrDefault();
                                    if (al == null) throw new System.Exception("No alignments found in drawing!");

                                    ObjectIdCollection idCol = new ObjectIdCollection(new Oid[1] { br.Id });

                                    dot.MoveBelow(idCol, al.Id);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                prdDbg(ex.ToString());
                                extTx.Abort();
                                throw;
                            }

                            extTx.Commit();
                        }
                        #endregion

                        extDb.SaveAs(extDb.Filename, true, DwgVersion.Current, null);
                    }
                    System.Windows.Forms.Application.DoEvents();
                }
                #endregion
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n" + ex.Message);
                return;
            }
        }

        [CommandMethod("APPLYCOLORSTODWGS")]
        public void applycolorstodwgs()
        {
            DocumentCollection docCol = Application.DocumentManager;
            Database localDb = docCol.MdiActiveDocument.Database;
            Editor editor = docCol.MdiActiveDocument.Editor;
            Document doc = docCol.MdiActiveDocument;
            CivilDocument civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

            try
            {
                #region Operation

                string path = string.Empty;
                OpenFileDialog dialog = new OpenFileDialog()
                {
                    Title = "Choose txt file:",
                    DefaultExt = "txt",
                    Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*",
                    FilterIndex = 0
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    path = dialog.FileName;
                }
                else return;

                List<string> fileList;
                fileList = File.ReadAllLines(path).ToList();
                path = Path.GetDirectoryName(path) + "\\";

                foreach (string name in fileList)
                {
                    prdDbg(name);
                    string fileName = path + name;

                    using (Database extDb = new Database(false, true))
                    {
                        extDb.ReadDwgFile(fileName, System.IO.FileShare.ReadWrite, false, "");

                        using (Transaction extTx = extDb.TransactionManager.StartTransaction())
                        {
                            colorizealllerlayersmethod(extDb);

                            extTx.Commit();
                        }
                        extDb.SaveAs(extDb.Filename, true, DwgVersion.Current, null);
                    }
                    System.Windows.Forms.Application.DoEvents();
                }
                #endregion
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\n" + ex.ToString());
                return;
            }
        }

        [CommandMethod("colorviewframes")]
        public void colorviewframes()
        {

            DocumentCollection docCol = Application.DocumentManager;
            Database localDb = docCol.MdiActiveDocument.Database;
            Editor editor = docCol.MdiActiveDocument.Editor;
            Document doc = docCol.MdiActiveDocument;
            CivilDocument civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

            using (Transaction tx = localDb.TransactionManager.StartTransaction())
            {
                try
                {
                    #region XrefNodeMethod
                    //XrefGraph graph = localDb.GetHostDwgXrefGraph(false);

                    ////skip node zero, hence i=1
                    //for (int i = 1; i < graph.NumNodes; i++)
                    //{
                    //    XrefGraphNode node = graph.GetXrefNode(i);
                    //    if (node.Name.Contains("alignment"))
                    //    {
                    //        editor.WriteMessage($"\nXref: {node.Name}.");
                    //        node.
                    //    }
                    //} 
                    #endregion

                    var bt = (BlockTable)tx.GetObject(localDb.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tx.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (Oid id in ms)
                    {
                        var br = tx.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br != null)
                        {
                            var bd = (BlockTableRecord)tx.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                            if (bd.IsFromExternalReference)
                            {
                                var xdb = bd.GetXrefDatabase(false);
                                if (xdb != null)
                                {
                                    string fileName = xdb.Filename;
                                    if (fileName.Contains("_alignment"))
                                    {
                                        editor.WriteMessage($"\n{xdb.Filename}.");
                                        if (IsFileLockedOrReadOnly(new FileInfo(fileName)))
                                        {
                                            editor.WriteMessage("\nUnable to modify the external reference. " +
                                                                  "It may be open in the editor or read-only.");
                                        }
                                        else
                                        {
                                            using (var xf = XrefFileLock.LockFile(xdb.XrefBlockId))
                                            {
                                                //Make sure the original symbols are loaded
                                                xdb.RestoreOriginalXrefSymbols();
                                                // Depending on the operation you're performing,
                                                // you may need to set the WorkingDatabase to
                                                // be that of the Xref
                                                //HostApplicationServices.WorkingDatabase = xdb;

                                                using (Transaction xTx = xdb.TransactionManager.StartTransaction())
                                                {
                                                    try
                                                    {
                                                        CivilDocument stylesDoc = CivilDocument.GetCivilDocument(xdb);

                                                        //View Frame Styles edit
                                                        Oid vfsId = stylesDoc.Styles.ViewFrameStyles["Basic"];
                                                        ViewFrameStyle vfs = xTx.GetObject(vfsId, OpenMode.ForWrite) as ViewFrameStyle;
                                                        DisplayStyle planStyle = vfs.GetViewFrameBoundaryDisplayStylePlan();
                                                        planStyle.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);

                                                        string layName = "C-ANNO-VFRM";

                                                        LayerTable lt = xTx.GetObject(xdb.LayerTableId, OpenMode.ForRead)
                                                            as LayerTable;
                                                        if (lt.Has(layName))
                                                        {
                                                            LayerTableRecord ltr = xTx.GetObject(lt[layName], OpenMode.ForWrite)
                                                                as LayerTableRecord;
                                                            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 7);
                                                        }
                                                    }
                                                    catch (System.Exception)
                                                    {
                                                        xTx.Abort();
                                                        tx.Abort();
                                                        return;
                                                        //throw;
                                                    }

                                                    xTx.Commit();
                                                }
                                                // And then set things back, afterwards
                                                //HostApplicationServices.WorkingDatabase = db;
                                                xdb.RestoreForwardingXrefSymbols();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    string viewFrameLayerName = "C-ANNO-VFRM";

                    LayerTable localLt = tx.GetObject(localDb.LayerTableId, OpenMode.ForRead)
                        as LayerTable;

                    short[] sequenceInit = new short[] { 1, 3, 4, 5, 6, 40 };
                    LinkedList<short> colorSequence = new LinkedList<short>(sequenceInit);
                    LinkedListNode<short> curNode;
                    curNode = colorSequence.First;

                    foreach (Oid id in localLt)
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tx.GetObject(id, OpenMode.ForRead);

                        if (ltr.Name.Contains("_alignment") &&
                            ltr.Name.Contains(viewFrameLayerName) &&
                            !ltr.Name.Contains("TEXT"))
                        {
                            editor.WriteMessage($"\n{ltr.Name}");
                            ltr.UpgradeOpen();
                            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 0);
                            //ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, curNode.Value);

                            //if (curNode.Next == null) curNode = colorSequence.First;
                            //else curNode = curNode.Next;

                        }
                    }
                }
                catch (System.Exception ex)
                {
                    tx.Abort();
                    editor.WriteMessage("\n" + ex.Message);
                    return;
                }
                tx.Commit();
            }
        }


    }
}