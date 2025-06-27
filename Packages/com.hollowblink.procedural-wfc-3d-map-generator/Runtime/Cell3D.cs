using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WFC3DMapGenerator
{
    public class Cell3D : MonoBehaviour
    {
        public bool collapsed;
        public Tile3D[] tileOptions;

        public void CreateCell(bool collapseState, Tile3D[] tiles, int cellIndex)
        {
            collapsed = collapseState;
            tileOptions = tiles;
        }

        public void RecreateCell(Tile3D[] tiles)
        {
            tileOptions = tiles;
        }

        public void RecreateCell(Tile3D selectedTile)
        {
            tileOptions = new Tile3D[] { selectedTile };
        }
    }
}
