using System.Collections.Generic;
using UnityEngine;

namespace WFC3DMapGenerator
{
    public class Tileset : ScriptableObject
    {
        [SerializeField] public List<Tile3D> tiles = new List<Tile3D>();
        [SerializeField] public float tileSize = 1;
        [SerializeField] public int tileCount = 0;
        [SerializeField] public List<string> tileTypes = new List<string>();
        [SerializeField] public List<string> socketTypes = new List<string>();
    }
}
