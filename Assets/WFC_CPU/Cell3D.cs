using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tile3D = WFC3DMapGenerator.Tile3D;

public class Cell3D : MonoBehaviour
{
    public bool collapsed;
    public Tile3D[] tileOptions;
    public ulong possibleTilesMask;
    public int entropy;
    public int index; //debug
    
#region Legacy methods (without bitmask)
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
#endregion

#region Bitmask methods
    public void CreateCell(bool collapseState, ulong mask, int startingEntropy, int cellIndex)
    {
        collapsed = collapseState;
        possibleTilesMask = mask;
        entropy = startingEntropy;
        index = cellIndex;
    }


    public void RecreateCell(ulong mask, int newEntropy)
    {
        possibleTilesMask = mask;
        entropy = newEntropy;
    }
#endregion
}
