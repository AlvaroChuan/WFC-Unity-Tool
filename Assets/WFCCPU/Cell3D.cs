using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tile3D = WFC3DMapGenerator.Tile3D;

public class Cell3D : MonoBehaviour
{
    public bool collapsed;
    public Tile3D[] tileOptions;
    public bool haSidoVisitado; //debug
    public bool visitable = false;
    public bool tieneCiudad;
    public int index; //debug
    public bool blocked = false; //If is bloq, no tile can be spawned here

    public bool showDebugVisitableCells;
    

    public void CreateCell(bool collapseState, Tile3D[] tiles, int cellIndex)
    {
        collapsed = collapseState;
        tileOptions = tiles;
        haSidoVisitado = false;
        tieneCiudad = false;
        index = cellIndex;

        if (!showDebugVisitableCells) Destroy(transform.GetChild(0).gameObject);
    }

    public void RecreateCell(Tile3D[] tiles)
    {
        tileOptions = tiles;
    }

    public void MakeVisitable()
    {
        visitable = true;
        if (!collapsed && showDebugVisitableCells) GetComponentInChildren<MeshRenderer>().material.color = new Color32(255, 30, 0, 50); 
    }
}
