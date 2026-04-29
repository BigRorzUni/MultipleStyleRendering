using UnityEngine;

public static class GpuDebugState
{
    public static ComputeBuffer tileMaskBuffer;
    public static ComputeBuffer outputRectBuffer;
    public static ComputeBuffer outputMaskBuffer;
    public static ComputeBuffer outputVisibilityBuffer;
    public static ComputeBuffer outputCountBuffer;
    public static ComputeBuffer indirectArgsBuffer;

    public static ComputeBuffer visibilityBuffer;
    public static int visibilityCount;

    public static int tilesX;
    public static int tilesY;
    public static int tileSize;
    public static int bboxCount;
    public static int frameUpdated;

    public static void SetOutputBuffers(ComputeBuffer rects, ComputeBuffer masks, ComputeBuffer visibility, ComputeBuffer count, ComputeBuffer args)
    {
        outputRectBuffer = rects;
        outputMaskBuffer = masks;
        outputVisibilityBuffer = visibility;
        outputCountBuffer = count;
        indirectArgsBuffer = args;
    }

    public static void SetVisibilityBuffer(ComputeBuffer visibility, int count)
    {
        visibilityBuffer = visibility;
        visibilityCount = count;
        frameUpdated = Time.frameCount;
    }

    public static void SetTileBuffers(ComputeBuffer tileMask, int inTilesX, int inTilesY, int inTileSize, int inBBoxCount)
    {
        tileMaskBuffer = tileMask;
        tilesX = inTilesX;
        tilesY = inTilesY;
        tileSize = inTileSize;
        bboxCount = inBBoxCount;
        frameUpdated = Time.frameCount;
    }

    public static void Clear()
    {
        tileMaskBuffer = null;
        outputRectBuffer = null;
        outputMaskBuffer = null;
        outputVisibilityBuffer = null;
        outputCountBuffer = null;
        indirectArgsBuffer = null;

        visibilityBuffer = null;
        visibilityCount = 0;

        tilesX = 0;
        tilesY = 0;
        tileSize = 0;
        bboxCount = 0;
        frameUpdated = 0;
    }
}