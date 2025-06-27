using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFC3DMapGenerator
{
    public class Tile3D : MonoBehaviour
    {
        [Serializable]
        public struct Socket
        {
            public string socket_name;
            //for horizontal faces
            [Header("For HORIZONTAL faces")]
            public bool horizontalFace;
            public bool isSymmetric;
            public bool isFlipped;
            //for vertical faces
            [Header("For VERTICAL faces")]
            public bool verticalFace;
            public int rotationIndex;
            public bool rotationallyInvariant;
        }

        public string tileType;
        public int probability;

        [Header("Create rotated tiles")]
        public bool rotateRight;
        public bool rotate180;
        public bool rotateLeft;

        public Vector3 rotation;
        public Vector3 scale;
        public Vector3 positionOffset;

        public List<Tile3D> upNeighbours = new List<Tile3D>();
        public List<Tile3D> rightNeighbours = new List<Tile3D>();
        public List<Tile3D> downNeighbours = new List<Tile3D>();
        public List<Tile3D> leftNeighbours = new List<Tile3D>();
        public List<Tile3D> aboveNeighbours = new List<Tile3D>();    // Y+
        public List<Tile3D> belowNeighbours = new List<Tile3D>();    // Y-

        [Header("Excluded neighbours")]
        public List<string> excludedNeighboursUp = new List<string>();
        public List<string> excludedNeighboursRight = new List<string>();
        public List<string> excludedNeighboursDown = new List<string>();
        public List<string> excludedNeighboursLeft = new List<string>();

        [Tooltip("Para definir la direccion la derecha siempre ser� el eje X (rojo) y arriba ser� el eje Z (azul)")]
        [Header("Sockets")]
        public Socket upSocket;
        public Socket rightSocket;
        public Socket leftSocket;
        public Socket downSocket;
        public Socket aboveSocket;
        public Socket belowSocket;
    }
}
