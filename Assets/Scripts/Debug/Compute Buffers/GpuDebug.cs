using UnityEngine;
using System.Text;

[ExecuteAlways]
public class GpuDebug : MonoBehaviour
{
    [Header("Toggle")]
    public bool trigger = false;  

    [Header("Behaviour")]
    public bool printOnlyOccupied = true;
    public int maxTilesToPrint = 128;
    public int maxOutputRectsToPrint = 128;

  

    void Update()
    {
        if (trigger)
        {
            trigger = false;
            DumpAll();
        }
    }

    void DumpAll()
    {
        StringBuilder sb = new StringBuilder();

        DumpOutputBuffers(sb);
        sb.AppendLine();
        DumpTileBuffer(sb);


        Debug.Log(sb.ToString());
    }


    void DumpTileBuffer(StringBuilder sb)
    {
        ComputeBuffer tileMaskBuffer = GpuDebugState.tileMaskBuffer;

        if (tileMaskBuffer == null)
        {
            Debug.Log("=== GPU TILE DEBUG ===\nTile mask buffer: null");
            return;
        }

        int tilesX = GpuDebugState.tilesX;
        int tilesY = GpuDebugState.tilesY;
        int tileSize = GpuDebugState.tileSize;
        int tileCount = tilesX * tilesY;

        if (tileCount <= 0)
        {
            Debug.Log("=== GPU TILE DEBUG ===\nTile grid is empty");
            return;
        }

        uint[] tileMasks = new uint[tileCount];
        tileMaskBuffer.GetData(tileMasks, 0, 0, tileCount);

        sb.AppendLine("=== NPR GPU TILE DEBUG ===");
        sb.AppendLine($"Frame updated: {GpuDebugState.frameUpdated}");
        sb.AppendLine($"BBox count: {GpuDebugState.bboxCount}");
        sb.AppendLine($"Tile grid: {tilesX} x {tilesY}");
        sb.AppendLine($"Tile size: {tileSize}");

        int occupiedCount = 0;
        for (int i = 0; i < tileCount; i++)
        {
            if (tileMasks[i] != 0u)
                occupiedCount++;
        }

        sb.AppendLine($"Occupied tiles: {occupiedCount}/{tileCount}");

        int printed = 0;
        for (int i = 0; i < tileCount; i++)
        {
            uint mask = tileMasks[i];

            if (printOnlyOccupied && mask == 0u)
                continue;

            int tx = i % tilesX;
            int ty = i / tilesX;

            int px = tx * tileSize;
            int py = ty * tileSize;

            sb.AppendLine($"[{i}] tile=({tx},{ty}) px=({px},{py}) mask={mask} (0x{mask:X8})");
            printed++;

            if (printed >= maxTilesToPrint)
                break;
        }

        if (printed == 0)
            sb.AppendLine("No tiles printed");

        Debug.Log(sb.ToString());
    }

    void DumpOutputBuffers(StringBuilder sb)
    {
        ComputeBuffer rectBuffer = GpuDebugState.outputRectBuffer;
        ComputeBuffer maskBuffer = GpuDebugState.outputMaskBuffer;
        ComputeBuffer visibilityBuffer = GpuDebugState.outputVisibilityBuffer;
        ComputeBuffer countBuffer = GpuDebugState.outputCountBuffer;
        ComputeBuffer argsBuffer = GpuDebugState.indirectArgsBuffer;

        sb.AppendLine("=== GPU TILE OUTPUT DEBUG ===");

        if (rectBuffer == null)
        {
            sb.AppendLine("Output rect buffer: null");
            return;
        }

        uint outputCount = 0;
        if (countBuffer != null)
        {
            uint[] countData = new uint[1];
            countBuffer.GetData(countData, 0, 0, 1);
            outputCount = countData[0];
            sb.AppendLine($"Output count: {outputCount}");
        }
        else
        {
            sb.AppendLine("Output count buffer: null");
        }

        if (argsBuffer != null)
        {
            uint[] args = new uint[4];
            argsBuffer.GetData(args, 0, 0, 4);
            sb.AppendLine($"Indirect args: vertexCountPerInstance={args[0]}, instanceCount={args[1]}, startVertex={args[2]}, startInstance={args[3]}");
        }
        else
        {
            sb.AppendLine("Indirect args buffer: null");
        }

        if (outputCount == 0)
        {
            sb.AppendLine("No emitted output rects");
            return;
        }

        int readCount = Mathf.Min((int)outputCount, maxOutputRectsToPrint);

        Vector4[] rects = new Vector4[readCount];
        uint[] masks = new uint[readCount];
        uint[] visibility = new uint[readCount];

        rectBuffer.GetData(rects, 0, 0, readCount);

        if (maskBuffer != null)
            maskBuffer.GetData(masks, 0, 0, readCount);

        if (visibilityBuffer != null)
            visibilityBuffer.GetData(visibility, 0, 0, readCount);

        for (int i = 0; i < readCount; i++)
        {
            Vector4 r = rects[i];
            uint mask = maskBuffer != null ? masks[i] : 0u;
            uint vis = visibilityBuffer != null ? visibility[i] : 0u;

            sb.AppendLine(
                $"[{i}] rect=({r.x}, {r.y}, {r.z}, {r.w}) mask={mask} (0x{mask:X8}) vis={vis}"
            );
        }

        if (readCount < outputCount)
            sb.AppendLine($"... truncated, printed {readCount}/{outputCount}");
    }

}