using UnityEngine;

namespace WFC3DMapGenerator
{
    public class WFCStructs
    {
        public const int MAX_NEIGHBOURS = 50;
        public unsafe struct Tile3DStruct
        {
            /*
            |-------------------------------------------------------------------------------|
            | In order to be able to send data to the buffer, all the data within the struct|
            | must be blitable, that means that the size in memory for c# is exactly the    |
            | the same as in HLSL, for uint arrays we only need to ensure that they have    |
            | the a fixed size.                                                             |
            |-------------------------------------------------------------------------------|
            */
            public int probability;
            public Vector3 rotation;

            // Neighbours (these are the indexes of the tiles in the tileObjects array)
            public fixed int upNeighbours[MAX_NEIGHBOURS];
            public fixed int rightNeighbours[MAX_NEIGHBOURS];
            public fixed int downNeighbours[MAX_NEIGHBOURS];
            public fixed int leftNeighbours[MAX_NEIGHBOURS];
            public fixed int aboveNeighbors[MAX_NEIGHBOURS];
            public fixed int belowNeighbours[MAX_NEIGHBOURS];
        }
        public unsafe struct Cell3DStruct
        {
            public int colapsed;
            // Number of tiles that can be placed in the cell
            // (array lenghts are fixed we can't use .lenght)
            public int entropy;
            /* Possible tiles
            |-------------------------------------------------------------------------------|
            | The possible tiles are stored in a uint array, each uint containing the index |
            | of a tile in the tileObjects array.                                           |
            |-------------------------------------------------------------------------------|
            */
            public fixed int tileOptions[MAX_NEIGHBOURS];
        };
    }
}
