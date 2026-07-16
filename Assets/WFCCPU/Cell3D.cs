using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tile3D = WFC3DMapGenerator.Tile3D;

public class Cell3D : MonoBehaviour
{
    public bool collapsed;
    public ulong possibleTilesMask;
    public int entropy;
    public int index; //debug
    

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
}
