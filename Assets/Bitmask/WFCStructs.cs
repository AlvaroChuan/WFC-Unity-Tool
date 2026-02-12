using UnityEngine;

public class WFCStructs
{
    public struct Tile3DStruct
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
        public  int[] upNeighbours;
        public  int[] rightNeighbours;
        public  int[] downNeighbours;
        public  int[] leftNeighbours;
        public  int[] aboveNeighbors;
        public  int[] belowNeighbours;
    }
    public struct Cell3DStruct
    {
        public int colapsed;
        // Number of tiles that can be placed in the cell
        // (array lenghts are fixed we can't use .lenght)
        public int entropy;
        
        public ulong[][] validityMasks;
        public ulong currentPossibleMask;
    };
}
