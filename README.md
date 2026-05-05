# MultipleStyleRendering

## Setup

1. Clone the repository:
   ```bash
   git clone git@github.com:BigRorzUni/MultipleStyleRendering.git
   ```

2. Install Unity Hub.

3. Open Unity Hub:
   - Go to **Projects > Add > Add from disk**
   - Select the cloned project folder
   - Open using **Unity version 6000.3.2f1**

---

## Running the Project

1. Open a scene from:
   ```
   Assets/Scenes/
   ```
   - `FreeCartoonCity` (main demo)
   - `SampleScene` (simple scene)
   (The others are designed for the benchmarking scripts)

---

## Configuring the Pipeline

1. Navigate to:
   ```
   Assets/PC_Renderer/
   ```

2. Select the renderer asset.

3. In the Inspector:
   ```
   Multi Style Renderer Feature > Settings
   ```

4. Adjust:
   - Render mode (Fullscreen / CPU / GPU / Tiling)
   - Merging
   - Occlusion
   - Tile size
   - Test Mode
   - Debug Bounding Boxes

---

## Stylising Objects

1. Select an object in the scene.

2. In the Inspector, locate/add the `StylisedTag` component.

3. Assign styles using:
   - Style bitmask
   - Test mode options (for benchmarks)

---

## Running Benchmarks

### Build

1. Go to:
   ```
   File > Build Profiles
   ```

2. Select your platform.

3. Enable:
   - Development Build

4. Build the project and save as:
   ```
   UnityProfiling
   ```

---

### Run Script

Update APP_NAME and PROJECT_PATH in `run_benchmarks.sh`

From the root directory of the project, after building:

```bash
./run_benchmarks.sh [frames] [warmup]
```

Example:

```bash
./run_benchmarks.sh 1000 500
```

- `frames`: number of recorded frames  
- `warmup`: number of warm-up frames  

---

## Output

Results are written to:

```
ProfilingLogs/
```

Includes:
- `summary.csv` (aggregated results)
- per-run logs (frame timings)

For graphs run:
```
Graphs.ipynb
```