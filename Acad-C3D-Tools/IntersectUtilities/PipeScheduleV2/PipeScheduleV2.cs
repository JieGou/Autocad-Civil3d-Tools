﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Navigation;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

using IntersectUtilities.UtilsCommon;

using static IntersectUtilities.UtilsCommon.Utils;

namespace IntersectUtilities.PipeScheduleV2
{
    public static class PipeScheduleV2
    {
        private static IPipeTypeRepository _repository;

        static PipeScheduleV2()
        {
            LoadPipeTypeData(@"X:\AutoCAD DRI - 01 Civil 3D\PipeSchedule\");
        }

        #region Variables and dicts
        public static Dictionary<string, Type> typeDict = new Dictionary<string, Type>()
        {
            { "DN", typeof(PipeTypeDN) },
            { "ALUPEX", typeof(PipeTypeCommon) },
            { "CU", typeof(PipeTypeCommon) },
        };

        private static Dictionary<string, PipeSystemEnum> systemDict = new Dictionary<string, PipeSystemEnum>()
        {
            {"DN", PipeSystemEnum.Stål },
            {"ALUPEX", PipeSystemEnum.AluPex },
            {"CU", PipeSystemEnum.Kobberflex },
        };

        private static Dictionary<PipeSystemEnum, string> systemDictReversed = new Dictionary<PipeSystemEnum, string>()
        {
            {PipeSystemEnum.Stål, "DN" },
            {PipeSystemEnum.AluPex , "ALUPEX" },
            {PipeSystemEnum.Kobberflex , "CU" },
        };

        private static string pipeTypes = string.Join(
            "|", Enum.GetNames(typeof(PipeTypeEnum)).Select(x => x.ToUpper()));
        private static string pipeDataTypes = string.Join(
            "|", typeDict.Keys);
        private static Regex layerNameDataParser =
            new Regex($@"FJV-(?<TYPE>{pipeTypes})-(?<DATATYPE>{pipeDataTypes})(?<DN>\d+)");

        public static Dictionary<string, Type> columnTypeDict = new Dictionary<string, Type>()
        {
            {"DN", typeof(int)},
            {"PipeType", typeof(string)},
            {"PipeSeries" , typeof(string)},
            {"pOd", typeof(double)},
            {"kOd", typeof(double)},
            {"tWdth", typeof(double)},
            {"minElasticRadii", typeof(double)},
            {"VpMax12", typeof(double)},
            {"VpMax16", typeof(double)},
        };

        public static void LoadPipeTypeData(string pathToPipeTypesStore)
        {
            var csvs = System.IO.Directory.EnumerateFiles(
                pathToPipeTypesStore, "*.csv", System.IO.SearchOption.TopDirectoryOnly);

            _repository = new PipeTypeRepository();
            _repository.Initialize(new PipeTypeDataLoaderCSV().Load(csvs));
        }

        public static void ListAllPipeTypes() => prdDbg(string.Join("\n", _repository.ListAllPipeTypes())); 
        #endregion

        #region Pipe schedule methods
        private static string ExtractLayerName(Entity ent)
        {
            string layer = ent.Layer;
            if (layer.Contains("|")) return layer.Split('|').Last();
            else return layer;
        }
        private static string ExtractLayerName(string layer)
        {
            if (layer.Contains("|")) return layer.Split('|').Last();
            else return layer;
        }
        public static int GetPipeDN(string layer)
        {
            layer = ExtractLayerName(layer);
            if (layerNameDataParser.IsMatch(layer))
            {
                var dnstring = layerNameDataParser.Match(layer).Groups["DN"].Value;
                int dn;
                if (int.TryParse(dnstring, out dn)) return dn;
                return 0;
            }
            else
            {
                prdDbg($"Layer name {layer} failed to provide DN!");
                return 0;
            }
        }
        public static int GetPipeDN(Entity ent)
        {
            return GetPipeDN(ExtractLayerName(ent));
        }
        public static PipeTypeEnum GetPipeType(string layer)
        {
            layer = ExtractLayerName(layer);
            if (layerNameDataParser.IsMatch(layer))
            {
                var typeString = layerNameDataParser.Match(layer).Groups["TYPE"].Value;
                if (Enum.TryParse(typeString, true, out PipeTypeEnum type)) return type;
                return PipeTypeEnum.Ukendt;
            }
            return PipeTypeEnum.Ukendt;
        }
        public static PipeTypeEnum GetPipeType(Entity ent, bool FRtoEnkelt = false)
        {
            var type = GetPipeType(ExtractLayerName(ent));
            if (!FRtoEnkelt) return type;
            switch (type)
            {
                case PipeTypeEnum.Frem:
                case PipeTypeEnum.Retur:
                    return PipeTypeEnum.Enkelt;
                default: return type;
            }
        }
        public static PipeTypeEnum GetPipeType(Entity ent)
        {
            return GetPipeType(ExtractLayerName(ent));
        }
        public static PipeSystemEnum GetPipeSystem(Entity ent)
        {
            string layer = ExtractLayerName(ent);
            if (layerNameDataParser.IsMatch(layer))
            {
                var systemString = layerNameDataParser.Match(layer).Groups["DATATYPE"].Value;
                PipeSystemEnum system;
                if (systemDict.TryGetValue(systemString, out system)) return system;
                return PipeSystemEnum.Ukendt;
            }
            return PipeSystemEnum.Ukendt;
        }
        public static double GetPipeOd(Entity ent)
        {
            int dn = GetPipeDN(ent);
            PipeSystemEnum system = GetPipeSystem(ent);
            if (!systemDictReversed.ContainsKey(system)) return 0;
            var pipeType = _repository.GetPipeType(systemDictReversed[system]);
            return pipeType.GetPipeOd(dn);
        }
        public static double GetPipeKOd(Entity ent, PipeSeriesEnum pipeSeries)
        {
            PipeTypeEnum type = GetPipeType(ent, true);
            PipeSystemEnum system = GetPipeSystem(ent);

            if (!systemDictReversed.ContainsKey(system)) return 0.0;
            var pipeType = _repository.GetPipeType(systemDictReversed[system]);

            int dn = GetPipeDN(ent);

            return pipeType.GetPipeKOd(dn, type, pipeSeries);
        }
        public static double GetPipeKOd(Entity ent, bool hardFail = false)
        {
            var series = GetPipeSeriesV2(ent, hardFail);
            var kOd = GetPipeKOd(ent, series);
            if (kOd == 0) prdDbg($"Ent {ent.Handle} has 0 kOd!");
            return kOd;
        }
        public static PipeSeriesEnum GetPipeSeriesV2(Entity ent, bool hardFail = false)
        {
            if (ent is BlockReference br)
            {
                prdDbg($"GetPipeSeriesV2 cannot be called on a BlockReference! {br.RealName()} - {br.Handle}");
                throw new Exception();
            }

            double realKod;
            try
            {
                realKod = ((Polyline)ent).ConstantWidth * 1000;
            }
            catch (Exception)
            {
                if (hardFail) throw new Exception($"Ent {ent.Handle} ConstantWidth threw an exception!");
                prdDbg($"Ent {ent.Handle} ConstantWidth threw an exception!");
                return PipeSeriesEnum.Undefined;
            }

            PipeSystemEnum system = GetPipeSystem(ent);
            if (!systemDictReversed.ContainsKey(system))
            {
                if (hardFail) throw new Exception(
                    $"Ent {ent.Handle} has an undefined PipeSystem {system}!");
                return PipeSeriesEnum.Undefined;
            }
            var pipeType = _repository.GetPipeType(systemDictReversed[system]);

            PipeTypeEnum type = GetPipeType(ent, true);
            int dn = GetPipeDN(ent);

            PipeSeriesEnum series = pipeType.GetPipeSeries(dn, type, realKod);

            if (hardFail && series == PipeSeriesEnum.Undefined)
                throw new Exception($"Determination of Series failed for pipe: {ent.Handle}!");
            return series;
        }
        public static double GetPipeStdLength(Entity ent) => GetPipeDN(ent) <= 80 ? 12 : 16;
        public static bool IsInSituBent(Entity ent)
        {
            PipeTypeEnum type = GetPipeType(ent);
            PipeSystemEnum system = GetPipeSystem(ent);

            //Flexrør er altid insitu bukkede
            if (
                system == PipeSystemEnum.Kobberflex ||
                system == PipeSystemEnum.AluPex
                ) return true;

            switch (type)
            {
                case PipeTypeEnum.Ukendt:
                    throw new Exception(
                        $"IsInSituBent -> Entity handle {ent.Handle} has invalid layer!");
                case PipeTypeEnum.Twin:
                    if (GetPipeDN(ent) < 65) return true;
                    break;
                case PipeTypeEnum.Frem:
                case PipeTypeEnum.Retur:
                    if (GetPipeDN(ent) < 100) return true;
                    break;
                default:
                    throw new Exception(
                        $"IsInSituBent -> Entity handle {ent.Handle} has invalid layer!");
            }
            return false;
        }
        public static double GetPipeMinElasticRadius(Entity ent, bool considerInSituBending = true)
        {
            if (considerInSituBending && IsInSituBent(ent)) return 0;

            PipeTypeEnum type = GetPipeType(ent, true);
            PipeSeriesEnum series = GetPipeSeriesV2(ent);
            PipeSystemEnum system = GetPipeSystem(ent);

            if (!systemDictReversed.ContainsKey(system)) return 0;
            var pipeType = _repository.GetPipeType(systemDictReversed[system]);

            int dn = GetPipeDN(ent);

            var rad = pipeType.GetMinElasticRadius(dn, type, series);

            return rad;
        }
        public static double GetBuerorMinRadius(Entity ent)
        {
            PipeSystemEnum system = GetPipeSystem(ent);
            PipeTypeEnum type = GetPipeType(ent, true);
            
            if (!systemDictReversed.ContainsKey(system)) return 0;
            var pipeType = _repository.GetPipeType(systemDictReversed[system]);

            int dn = GetPipeDN(ent);
            int std = (int)GetPipeStdLength(ent);

            double rad = pipeType.GetBuerorMinRadius(dn, std);

            return rad;
        }
        #endregion
    }
}
