using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tile3D = WFC3DMapGenerator.Tile3D;

public class Cell3D : MonoBehaviour
{
    public bool collapsed;
    public Tile3D[] tileOptions;
    public int index; //debug
    

    public void CreateCell(bool collapseState, Tile3D[] tiles, int cellIndex)
    {
        collapsed = collapseState;
        tileOptions = tiles;
        index = cellIndex;
    }

    public void RecreateCell(Tile3D[] tiles)
    {
        tileOptions = tiles;
    }
}
