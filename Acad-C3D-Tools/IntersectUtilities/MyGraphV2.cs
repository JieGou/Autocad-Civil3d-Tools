﻿using Autodesk.AutoCAD.DatabaseServices;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static IntersectUtilities.UtilsCommon.Utils;

namespace IntersectUtilities
{
    internal class GraphNodeV2
    {
        public Pipeline Node { get; set; }
        public List<GraphNodeV2> Children { get; set; }

        public GraphNodeV2(Pipeline node)
        {
            Node = node;
            Children = new List<GraphNodeV2>();
        }

        public static GraphNodeV2 CreateGraph(List<Pipeline> group, double tolerance)
        {
            // Find the pipeline with the largest pipe size
            Pipeline rootPipeline = group.OrderByDescending(pipeline => pipeline.MaxDn).First();

            // Create a dictionary to hold the graph nodes for each pipeline
            var nodes = group.ToDictionary(pipeline => pipeline, pipeline => new GraphNodeV2(pipeline));

            // Connect the graph nodes based on the IsConnected method of the pipelines
            foreach (var pipeline in group)
            {
                var node = nodes[pipeline];

                foreach (var otherPipeline in group)
                {
                    // Ensure that we do not create a cycle in the graph
                    if (pipeline != otherPipeline && pipeline.IsConnectedTo(otherPipeline, tolerance))
                    {
                        nodes[otherPipeline].Children.Add(node);
                        nodes[pipeline].Children.Remove(nodes[otherPipeline]); // Ensure the child does not have the current node as a child
                    }
                }
            }

            // Return the root node of the graph
            return nodes[rootPipeline];
        }

        public static void ToDot(List<GraphNodeV2> rootNodes)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("digraph G {");

            int subGraphId = 1;
            foreach (GraphNodeV2 rootNode in rootNodes)
            {
                // Start a new subgraph for this group of pipelines
                sb.AppendLine($"subgraph G_{subGraphId} {{");
                sb.AppendLine("node [shape=record, fontname=\"monospace bold\"];");

                HashSet<GraphNodeV2> visitedForEdges = new HashSet<GraphNodeV2>();
                visitedForEdges.Add(rootNode);

                Stack<GraphNodeV2> stack = new Stack<GraphNodeV2>();
                stack.Push(rootNode);
                //Print edges
                int count = 0;
                while (stack.Count > 0)
                {
                    count++;
                    GraphNodeV2 current = stack.Pop();
                    foreach (GraphNodeV2 child in current.Children)
                        child.Node.CheckReverseDirection(current.Node);
                    current.Node.EstablishCellConnections(current.Children);

                    sb.AppendLine(
                        $"node{current.Node.PipelineNumber} " +
                        $"[label=\"{{{current.Node.Alignment.Name}\n" +
                        $"{current.Node.GetEncompassingLabel()}" +
                        $"}}\"];");

                    foreach (GraphNodeV2 child in current.Children) stack.Push(child);
                    if (count > 1000) break;
                }

                stack.Clear();
                stack.Push(rootNode);
                //Print nodes and labels
                count = 0;
                while (stack.Count > 0)
                {
                    count++;
                    GraphNodeV2 current = stack.Pop();
                    
                    sb.AppendLine(current.Node.GetEdges());

                    foreach (GraphNodeV2 child in current.Children) stack.Push(child);
                    if (count > 1000) break;
                }

                

                sb.AppendLine("  }");

                subGraphId++;
            }

            sb.AppendLine("}");

            //Check or create directory
            if (!Directory.Exists(@"C:\Temp\"))
                Directory.CreateDirectory(@"C:\Temp\");

            //Write the collected graphs to one file
            using (System.IO.StreamWriter file = new System.IO.StreamWriter($"C:\\Temp\\MyGraph.dot"))
            {
                file.WriteLine(sb.ToString()); // "sb" is the StringBuilder
            }

            //Start the dot engine to create the graph
            System.Diagnostics.Process cmd = new System.Diagnostics.Process();
            cmd.StartInfo.FileName = "cmd.exe";
            cmd.StartInfo.WorkingDirectory = @"C:\Temp\";
            cmd.StartInfo.Arguments = @"/c ""dot -Tpdf MyGraph.dot > MyGraph.pdf""";
            cmd.Start();
        }
    }
}
