using System;
using System.Collections.Generic;
using UnityEngine;
using Cell3DStruct = WFC3DMapGenerator.WFCStructs.Cell3DStruct;

namespace WFC3DMapGenerator
{
    public class GridUtils : MonoBehaviour
    {
        /// <summary>
        /// Extracts a subgrid from the original grid based on the start coordinates and dimensions of the subgrid.
        /// </summary>
        /// <param name="startCoords"></param> Initial coordinates of the subgrid in the original grid.
        /// <param name="subGridDimensions"></param> Dimensions of the subgrid to be extracted.
        /// <param name="ogGrid"></param> The original grid from which the subgrid will be extracted.
        /// <param name="ogGridDimensions"></param> Dimensions of the original grid.
        /// <returns></returns> Returns a tuple containing the extracted subgrid and the indices of the original grid that correspond to the subgrid.
        public static Tuple<Cell3DStruct[], int[]> ExtractSubGrid(Vector3Int startCoords, ref Vector3Int subGridDimensions, Cell3DStruct[] ogGrid, Vector3Int ogGridDimensions)
        {
            // Clamp to matrix bounds
            if (startCoords.x < 0) subGridDimensions.x = subGridDimensions.x + startCoords.x;
            if (startCoords.y < 0) subGridDimensions.y = subGridDimensions.y + startCoords.y;
            if (startCoords.z < 0) subGridDimensions.z = subGridDimensions.z + startCoords.z;
            startCoords.x = Mathf.Max(0, startCoords.x);
            startCoords.y = Mathf.Max(0, startCoords.y);
            startCoords.z = Mathf.Max(0, startCoords.z);
            subGridDimensions.x = Mathf.Min(subGridDimensions.x, ogGridDimensions.x - startCoords.x);
            subGridDimensions.y = Mathf.Min(subGridDimensions.y, ogGridDimensions.y - startCoords.y);
            subGridDimensions.z = Mathf.Min(subGridDimensions.z, ogGridDimensions.z - startCoords.z);

            // Extract the subgrid
            List<Cell3DStruct> subGrid = new List<Cell3DStruct>();
            List<int> subGridIndices = new List<int>();
            for (int y = startCoords.y; y < startCoords.y + subGridDimensions.y; y++)
            {
                for (int z = startCoords.z; z < startCoords.z + subGridDimensions.z; z++)
                {
                    for (int x = startCoords.x; x < startCoords.x + subGridDimensions.x; x++)
                    {
                        subGrid.Add(ogGrid[x + z * ogGridDimensions.x + y * ogGridDimensions.x * ogGridDimensions.z]);
                        subGridIndices.Add(x + z * ogGridDimensions.x + y * ogGridDimensions.x * ogGridDimensions.z);
                    }
                }
            }
            return new Tuple<Cell3DStruct[], int[]>(subGrid.ToArray(), subGridIndices.ToArray());
        }

        /// <summary>
        /// Converts 3D coordinates to a 1D index based on the grid dimensions.
        /// </summary>
        /// <param name="coords"></param> Coordinates to convert.
        /// <param name="gridDimensions"></param> Dimensions of the grid.
        /// <returns></returns>
        public static int GetIndexFromCoords(Vector3Int coords, Vector3Int gridDimensions)
        {
            // Clamp to matrix bounds
            coords.x = Mathf.Max(0, coords.x);
            coords.y = Mathf.Max(0, coords.y);
            coords.z = Mathf.Max(0, coords.z);
            coords.x = Mathf.Min(coords.x, gridDimensions.x - 1);
            coords.y = Mathf.Min(coords.y, gridDimensions.y - 1);
            coords.z = Mathf.Min(coords.z, gridDimensions.z - 1);

            return coords.x + coords.z * gridDimensions.x + coords.y * gridDimensions.x * gridDimensions.z;
        }

        /// <summary>
        /// Combines a subgrid with the original grid at the specified indices.
        /// </summary>
        /// <param name="grid"></param> The original grid to which the subgrid will be combined.
        /// <param name="subGrid"></param> The subgrid to be combined with the original grid.
        /// <param name="subGridIndices"></param> Indices of the original grid that correspond to the subgrid.
        public static void CombineGridWithSubgrid(Cell3DStruct[] grid, Cell3DStruct[] subGrid, int[] subGridIndices)
        {
            for (int i = 0; i < subGrid.Length; i++) grid[subGridIndices[i]] = subGrid[i];
        }
    }
}