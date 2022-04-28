﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using IntersectUtilities;
using IntersectUtilities.UtilsCommon;
using LERImporter.Schema;
using Microsoft.VisualBasic.Logging;
using Log = LERImporter.SimpleLogger;
using Oid = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using static IntersectUtilities.UtilsCommon.UtilsDataTables;
using DataTable = System.Data.DataTable;
using static IntersectUtilities.UtilsCommon.Utils;

namespace LERImporter
{
    internal class ConsolidatedCreator
    {
        public static void CreateLerData(Database db, FeatureCollection fc)
        {
            string pathKrydsninger = "X:\\AutoCAD DRI - 01 Civil 3D\\Krydsninger.csv";
            DataTable dtKrydsninger = CsvReader.ReadCsvToDataTable(pathKrydsninger, "Krydsninger");
            if (dtKrydsninger == null) throw new System.Exception("Krydsninger could not be read!");

            HashSet<UtilityOwner> ownersRegister = new HashSet<UtilityOwner>();
            HashSet<LedningType> ledninger = new HashSet<LedningType>();
            HashSet<LedningstraceType> ledningstrace = new HashSet<LedningstraceType>();
            HashSet<LedningskomponentType> ledningskomponenter = new HashSet<LedningskomponentType>();

            #region Redirect objects to collections
            //Redirect objects to collections
            //int i = 0;
            foreach (FeatureMember fm in fc.featureMember)
            {
                //i++; prdDbg($"Switching item {i}.");
                System.Windows.Forms.Application.DoEvents();
                switch (fm.item)
                {
                    case UtilityOwner uo:
                        ownersRegister.Add(uo);
                        break;
                    case Graveforesp gvfsp:
                        break;
                    case UtilityPackageInfo upi:
                        break;
                    case Kontaktprofil kp:
                        break;
                    case Informationsressource ir:
                        break;
                    case LedningType lt:
                        ledninger.Add(lt);
                        //prdDbg(lt.gmlid);
                        break;
                    case LedningstraceType ltr:
                        ledningstrace.Add(ltr);
                        //prdDbg(ltr.gmlid);
                        break;
                    case LedningskomponentType lk:
                        ledningskomponenter.Add(lk);
                        //prdDbg(lk.gmlid);
                        break;
                    default:
                        prdDbg(fm.item.GMLTypeID);
                        throw new System.Exception($"Unexpected type encountered {fm.item.GetType().Name}!");
                }
            } 
            #endregion

            #region Populate Company Name
            //Populate Company Name
            foreach (LedningType ledning in ledninger)
            {
                var owner = ownersRegister.FirstOrDefault(x => x.ledningsejer == ledning.ledningsejer);
                //if (owner == default) throw new System.Exception($"Ledning {ledning.id} kan ikke finde ejer!");
                if (owner == default) prdDbg($"Ledning {ledning.id} kan ikke finde ejer!");
                else ledning.LedningsEjersNavn = owner.companyName;
            }
            foreach (LedningstraceType trace in ledningstrace)
            {
                var owner = ownersRegister.FirstOrDefault(x => x.ledningsejer == trace.ledningsejer);
                //if (owner == default) throw new System.Exception($"Ledning {trace.id} kan ikke finde ejer!");
                if (owner == default) prdDbg($"Ledning {trace.id} kan ikke finde ejer!");
                else trace.LedningsEjersNavn = owner.companyName;
            }
            foreach (LedningskomponentType komp in ledningskomponenter)
            {
                var owner = ownersRegister.FirstOrDefault(x => x.ledningsejer == komp.ledningsejer);
                //if (owner == default) throw new System.Exception($"Ledning {komp.id} kan ikke finde ejer!");
                if (owner == default) prdDbg($"Ledning {komp.id} kan ikke finde ejer!");
                else komp.LedningsEjersNavn = owner.companyName;
            }
            #endregion

            #region Create property sets
            //Dictionary to translate between type name and psName
            Dictionary<string, string> psDict = new Dictionary<string, string>();

            //Create property sets
            HashSet<Type> allUniqueTypes = ledninger.Select(x => x.GetType()).Distinct().ToHashSet();
            allUniqueTypes.UnionWith(ledningstrace.Select(x => x.GetType()).Distinct().ToHashSet());
            allUniqueTypes.UnionWith(ledningskomponenter.Select(x => x.GetType()).Distinct().ToHashSet());
            foreach (Type type in allUniqueTypes)
            {
                string psName = type.Name.Replace("Type", "");
                //Store the ps name in dictionary referenced by the type name
                //PS name is not goood! It becomes Elledning which is not unique
                //But it is unique!!
                //Data with different files will still follow the class definition in code
                //Which assures that all pssets are the same
                psDict.Add(type.Name, psName);

                PropertySetDefinition propSetDef = new PropertySetDefinition();
                propSetDef.SetToStandard(db);
                propSetDef.SubSetDatabaseDefaults(db);

                propSetDef.Description = type.FullName;
                bool isStyle = false;
                var appliedTo = new StringCollection()
                {
                    RXClass.GetClass(typeof(Polyline)).Name,
                    RXClass.GetClass(typeof(Polyline3d)).Name,
                    RXClass.GetClass(typeof(DBPoint)).Name,
                    RXClass.GetClass(typeof(Hatch)).Name,
                };
                propSetDef.SetAppliesToFilter(appliedTo, isStyle);

                var properties = type.GetProperties();

                foreach (PropertyInfo prop in properties)
                {
                    bool include = prop.CustomAttributes.Any(x => x.AttributeType == typeof(Schema.PsInclude));
                    if (include)
                    {
                        var propDefManual = new PropertyDefinition();
                        propDefManual.SetToStandard(db);
                        propDefManual.SubSetDatabaseDefaults(db);
                        propDefManual.Name = prop.Name;
                        propDefManual.Description = prop.Name;
                        switch (prop.PropertyType.Name)
                        {
                            case nameof(String):
                                propDefManual.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                                propDefManual.DefaultData = "";
                                break;
                            case nameof(System.Boolean):
                                propDefManual.DataType = Autodesk.Aec.PropertyData.DataType.TrueFalse;
                                propDefManual.DefaultData = false;
                                break;
                            case nameof(Double):
                                propDefManual.DataType = Autodesk.Aec.PropertyData.DataType.Real;
                                propDefManual.DefaultData = 0.0;
                                break;
                            case nameof(Int32):
                                propDefManual.DataType = Autodesk.Aec.PropertyData.DataType.Integer;
                                propDefManual.DefaultData = 0;
                                break;
                            default:
                                propDefManual.DataType = Autodesk.Aec.PropertyData.DataType.Text;
                                propDefManual.DefaultData = "";
                                break;
                        }
                        propSetDef.Definitions.Add(propDefManual);
                    }
                }

                using (Transaction tx = db.TransactionManager.StartTransaction())
                {
                    //check if prop set already exists
                    DictionaryPropertySetDefinitions dictPropSetDef = new DictionaryPropertySetDefinitions(db);
                    if (dictPropSetDef.Has(psName, tx))
                    {
                        tx.Abort();
                        continue;
                    }
                    dictPropSetDef.AddNewRecord(psName, propSetDef);
                    tx.AddNewlyCreatedDBObject(propSetDef, true);
                    tx.Commit();
                }
            }
            #endregion

            #region Create elements
            //List of all (new) layers of new entities
            HashSet<string> layerNames = new HashSet<string>();

            foreach (LedningType ledning in ledninger)
            {
                string psName = psDict[ledning.GetType().Name];
                ILerLedning iLedning = ledning as ILerLedning;
                if (iLedning == null)
                    throw new System.Exception($"Ledning {ledning.id} har ikke implementeret ILerLedning!");
                ObjectId entityId = iLedning.DrawEntity2D(db);
                Entity ent = entityId.Go<Entity>(db.TransactionManager.TopTransaction, OpenMode.ForWrite);
                layerNames.Add(ent.Layer);

                //Attach the property set
                PropertySetManager.AttachNonDefinedPropertySet(db, ent, psName);

                //Populate the property set
                var psData = GmlToPropertySet.TranslateGmlToPs(ledning);
                PropertySetManager.PopulateNonDefinedPropertySet(db, ent, psName, psData);
            }
            foreach (LedningstraceType trace in ledningstrace)
            {
                string psName = psDict[trace.GetType().Name];
                ILerLedning ledning = trace as ILerLedning;
                if (ledning == null)
                    throw new System.Exception($"Trace {trace.id} har ikke implementeret ILerLedning!");
                ObjectId entityId = ledning.DrawEntity2D(db);
                Entity ent = entityId.Go<Entity>(db.TransactionManager.TopTransaction, OpenMode.ForWrite);
                layerNames.Add(ent.Layer);

                //Attach the property set
                PropertySetManager.AttachNonDefinedPropertySet(db, ent, psName);

                //Populate the property set
                var psData = GmlToPropertySet.TranslateGmlToPs(trace);
                PropertySetManager.PopulateNonDefinedPropertySet(db, ent, psName, psData);
            }
            foreach (LedningskomponentType komponent in ledningskomponenter)
            {
                string psName = psDict[komponent.GetType().Name];
                ILerKomponent creator = komponent as ILerKomponent;
                if (creator == null)
                    throw new System.Exception($"Komponent {komponent.id} har ikke implementeret ILerKomponent!");
                Oid entityId = creator.DrawComponent(db);
                Entity ent = entityId.Go<Entity>(db.TransactionManager.TopTransaction, OpenMode.ForWrite);

                //Attach the property set
                PropertySetManager.AttachNonDefinedPropertySet(db, ent, psName);

                //Populate the property set
                var psData = GmlToPropertySet.TranslateGmlToPs(komponent);
                PropertySetManager.PopulateNonDefinedPropertySet(db, ent, psName, psData);
            }
            #endregion

            #region Read and assign layer's color
            //Regex to parse the color information
            Regex colorRegex = new Regex(@"^(?<R>\d+)\*(?<G>\d+)\*(?<B>\d+)");

            //Cache layer table
            LayerTable ltable = db.LayerTableId.Go<LayerTable>(db.TransactionManager.TopTransaction);

            //Set up all LER layers
            foreach (string layerName in layerNames)
            {
                string colorString = ReadStringParameterFromDataTable(layerName, dtKrydsninger, "Farve", 0);

                Color color;
                if (colorString.IsNoE())
                {
                    Log.log($"Ledning with layer name {layerName} could not get a color!");
                    color = Color.FromColorIndex(ColorMethod.ByAci, 0);
                }
                else if (colorRegex.IsMatch(colorString))
                {
                    Match match = colorRegex.Match(colorString);
                    byte R = Convert.ToByte(int.Parse(match.Groups["R"].Value));
                    byte G = Convert.ToByte(int.Parse(match.Groups["G"].Value));
                    byte B = Convert.ToByte(int.Parse(match.Groups["B"].Value));
                    //prdDbg($"Set layer {name} to color: R: {R.ToString()}, G: {G.ToString()}, B: {B.ToString()}");
                    color = Color.FromRgb(R, G, B);
                }
                else
                {
                    Log.log($"Ledning layer name {layerName} could not parse colorString {colorString}!");
                    color = Color.FromColorIndex(ColorMethod.ByAci, 0);
                }

                LayerTableRecord ltr = ltable[layerName]
                    .Go<LayerTableRecord>(db.TransactionManager.TopTransaction, OpenMode.ForWrite);

                ltr.Color = color;
            }
            #endregion

            #region Read and assign layer's linetype
            LinetypeTable ltt = (LinetypeTable)db.TransactionManager.TopTransaction
                .GetObject(db.LinetypeTableId, OpenMode.ForWrite);

            //Check if all line types are present
            HashSet<string> missingLineTypes = new HashSet<string>();
            foreach (string layerName in layerNames)
            {
                string lineTypeName = ReadStringParameterFromDataTable(layerName, dtKrydsninger, "LineType", 0);
                if (lineTypeName.IsNoE()) continue;
                else if (!ltt.Has(lineTypeName)) missingLineTypes.Add(lineTypeName);
            }

            if (missingLineTypes.Count > 0)
            {
                Database ltDb = new Database(false, true);
                ltDb.ReadDwgFile("X:\\AutoCAD DRI - 01 Civil 3D\\Projection_styles.dwg",
                    FileOpenMode.OpenForReadAndAllShare, false, null);
                Transaction ltTx = ltDb.TransactionManager.StartTransaction();

                Oid destDbMsId = SymbolUtilityServices.GetBlockModelSpaceId(db);

                LinetypeTable sourceLtt = (LinetypeTable)ltDb.TransactionManager.TopTransaction
                    .GetObject(ltDb.LinetypeTableId, OpenMode.ForRead);
                ObjectIdCollection idsToClone = new ObjectIdCollection();

                foreach (string missingName in missingLineTypes) idsToClone.Add(sourceLtt[missingName]);

                IdMapping mapping = new IdMapping();
                ltDb.WblockCloneObjects(idsToClone, destDbMsId, mapping, DuplicateRecordCloning.Replace, false);
                ltTx.Commit();
                ltTx.Dispose();
                ltDb.Dispose();
            }

            Oid lineTypeId;
            foreach (string layerName in layerNames)
            {
                string lineTypeName = ReadStringParameterFromDataTable(layerName, dtKrydsninger, "LineType", 0);
                if (lineTypeName.IsNoE())
                {
                    Log.log($"WARNING! Layer name {layerName} does not have a line type specified!.");
                    //If linetype string is NoE -> CONTINUOUS linetype must be used
                    lineTypeId = ltt["Continuous"];
                }
                else
                {
                    //the presence of the linetype is assured in previous foreach.
                    lineTypeId = ltt[lineTypeName];
                }
                LayerTableRecord ltr = ltable[layerName]
                        .Go<LayerTableRecord>(db.TransactionManager.TopTransaction, OpenMode.ForWrite);
                ltr.LinetypeObjectId = lineTypeId;
            }
            #endregion
        }
    }
}