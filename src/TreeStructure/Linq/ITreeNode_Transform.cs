﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TreeStructures.Xml.Serialization;
using System.Xml.Serialization;

namespace TreeStructures.Linq {
    public static partial class TreeNodeExtenstions {

        #region 変換
        /// <summary>Gets a collection of pairs consisting of data generated from each node and its index.</summary>
        /// <typeparam name="U">Type of the data to generate</typeparam>
        /// <typeparam name="T">Type of each node</typeparam>
        /// <param name="self">Starting node</param>
        /// <param name="conv">Conversion function</param>
        /// <returns>A dictionary containing pairs of node index and the generated data</returns>
        public static Dictionary<NodeIndex, U> ToNodeMap<T, U>(this ITreeNode<T> self, Func<T, U> conv)
        where T : ITreeNode<T> {
            return self.Levelorder().ToDictionary(x => x.NodeIndex(), x => conv(x));
        }
        /// <summary>Gets a collection of pairs consisting of each node and its index.</summary>
        /// <typeparam name="T">Type of each node</typeparam>
        /// <param name="self">Starting node</param>
        /// <returns>A dictionary containing pairs of node index and the node</returns>
        public static Dictionary<NodeIndex, T> ToNodeMap<T>(this ITreeNode<T> self)
        where T : ITreeNode<T> {
            return self.ToNodeMap(x => x);
        }
        /// <summary>Gets a collection of pairs consisting of each node's serialized instance and its index, as a collection that can be serialized by <see cref="XmlSerializer"/>.</summary>
        /// <typeparam name="U">Type of the object to be serialized</typeparam>
        /// <typeparam name="T">Type of each node</typeparam>
        /// <param name="self">Starting node</param>
        /// <param name="conv">Function to obtain a serialized object from each node</param>
        /// <returns>A SerializableNodeMap collection containing pairs of node index and the serialized object</returns>
        public static SerializableNodeMap<U> ToSerializableNodeMap<U, T>(this ITreeNode<T> self, Func<T, U> conv)
        where T : ITreeNode<T> {
            return new SerializableNodeMap<U>(self.ToNodeMap(conv));
        }
        /// <summary>Gets a collection of pairs consisting of each node and its index, as a collection that can be serialized by <see cref="XmlSerializer"/>.</summary>
        /// <typeparam name="T">Type of the node to be serialized</typeparam>
        /// <param name="self">Starting node</param>
        /// <returns>A SerializableNodeMap collection containing pairs of node index and the node itself</returns>
        public static SerializableNodeMap<T> ToSerializableNodeMap<T>(this ITreeNode<T> self)
        where T : ITreeNode<T> {
            return self.ToSerializableNodeMap(x => x);
        }
        /// <summary>Generates a tree diagram as a string.</summary>
        /// <typeparam name="T">Type of the node</typeparam>
        /// <param name="self">Current node</param>
        /// <param name="tostring">Function to convert a node to its string representation</param>
        /// <returns>Tree diagram</returns>
        public static string ToTreeDiagram<T>(this ITreeNode<T> self, Func<T, string> tostring) where T : ITreeNode<T> {
            string branch = "├ ";
            string lastBranch = "└ ";
            string through = "│ ";
            string blank = "  ";

            string createLine(IEnumerable<T> nodes, NodeIndex idx) {
                var line = string.Concat(nodes.Zip(idx).Select(pair => pair.First.IsRoot() ? "" : pair.First.HasNextSibling() ? through : blank));
                return line;
            }
            var strlit = new List<string>();
            foreach (var n in self.Preorder()) {
                var line = createLine(n.Upstream().Reverse(), n.NodeIndex());
                var head = n.IsRoot() ? "" : n.HasNextSibling() ? branch : lastBranch;
                line = line + head + tostring(n) + Environment.NewLine;
                strlit.Add(line);
            }
            return string.Concat(strlit);
        }
        internal static string ToPhylogeneticTree<T>(this ITreeNode<T> self, Func<T, string> tostring) where T : ITreeNode<T> {
            string node = "┬";
            string lastBranch = "└";
            string through = "─";
            string blank = "  ";
            string leafInnerBr = "┼";

            int len(string comment) => Encoding.GetEncoding("Shift_JIS").GetByteCount(comment);

            throw new NotImplementedException();
        }
        #endregion
    }
}
