---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Core Game Patterns

## Object Pooling

### Unity Built-in ObjectPool<T>
```csharp
public sealed class ProjectilePool : IDisposable
{
    readonly ObjectPool<Projectile> _pool;

    public ProjectilePool(Projectile prefab, Transform parent)
    {
        _pool = new ObjectPool<Projectile>(
            createFunc: () => { var obj = Object.Instantiate(prefab, parent); obj.Pool = this; return obj; },
            actionOnGet: p => p.gameObject.SetActive(true),
            actionOnRelease: p => { p.gameObject.SetActive(false); p.ResetState(); },
            actionOnDestroy: p => Object.Destroy(p.gameObject),
            collectionCheck: false, defaultCapacity: 20, maxSize: 100
        );
    }

    public Projectile Get() => _pool.Get();
    public void Release(Projectile p) => _pool.Release(p);
    public void Dispose() => _pool.Dispose();
}
```

### Generic Pool Service (VContainer)
```csharp
public sealed class PoolService : IDisposable
{
    readonly Dictionary<string, IDisposable> _pools = new();

    public ObjectPool<T> GetOrCreate<T>(string id, Func<T> create,
        Action<T>? onGet = null, Action<T>? onRelease = null,
        int capacity = 20, int max = 100) where T : class
    {
        if (_pools.TryGetValue(id, out var existing)) return (ObjectPool<T>)existing;
        var pool = new ObjectPool<T>(create, onGet, onRelease, defaultCapacity: capacity, maxSize: max);
        _pools[id] = pool;
        return pool;
    }

    public void Dispose() { foreach (var p in _pools.Values) p.Dispose(); _pools.Clear(); }
}
```

Pre-warm: get N items then release them all in `Initialize()`.

## State Machine

```csharp
public abstract class State<TContext>
{
    protected TContext Context { get; private set; }
    public void SetContext(TContext ctx) => Context = ctx;
    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Tick() { }
}

public sealed class StateMachine<TContext> : ITickable
{
    readonly TContext _context;
    readonly Dictionary<Type, State<TContext>> _states = new();
    State<TContext>? _current;

    public StateMachine(TContext context) => _context = context;
    public void AddState<TState>(TState state) where TState : State<TContext>
        { state.SetContext(_context); _states[typeof(TState)] = state; }
    public void TransitionTo<TState>() where TState : State<TContext>
        { _current?.Exit(); _current = _states[typeof(TState)]; _current.Enter(); }
    public void Tick() => _current?.Tick();
}

// Usage
public sealed class GameService : IInitializable, ITickable
{
    readonly StateMachine<GameService> _fsm;
    public GameService() {
        _fsm = new StateMachine<GameService>(this);
        _fsm.AddState(new MenuState());
        _fsm.AddState(new PlayState());
        _fsm.AddState(new PauseState());
        _fsm.AddState(new GameOverState());
    }
    public void Initialize() => _fsm.TransitionTo<MenuState>();
    public void Tick() => _fsm.Tick();
}
```

## Command Pattern (Undo/Redo)

```csharp
public interface ICommand { void Execute(); void Undo(); }

public sealed class CommandHistory
{
    readonly Stack<ICommand> _undoStack = new();
    readonly Stack<ICommand> _redoStack = new();

    public void Execute(ICommand cmd) { cmd.Execute(); _undoStack.Push(cmd); _redoStack.Clear(); }
    public void Undo() { if (_undoStack.Count == 0) return; var c = _undoStack.Pop(); c.Undo(); _redoStack.Push(c); }
    public void Redo() { if (_redoStack.Count == 0) return; var c = _redoStack.Pop(); c.Execute(); _undoStack.Push(c); }
}

public sealed class MoveCommand : ICommand
{
    readonly Transform _target; readonly Vector3 _from, _to;
    public MoveCommand(Transform t, Vector3 to) { _target = t; _from = t.position; _to = to; }
    public void Execute() => _target.position = _to;
    public void Undo() => _target.position = _from;
}
```

## Procedural Dynamic Mesh (polyline tube / rope / trail)

For geometry that follows a polyline and is **rebuilt every frame** (ropes, tubes, trails, cables), prefer a **single dynamic `Mesh`** over instancing N built-in primitives. Spawning `Graphics.RenderMeshInstanced` of `N` cylinders along the path is both heavier and visually broken — each cylinder is a full primitive (~3000 verts for a typical segment count) and adjacent segments leave gaps at the corners.

A single tube mesh sweeping mitered rings along the path is ~210 verts for the same path and has no corner gaps, because consecutive rings share a continuous surface.

```csharp
public sealed class TubeMeshBuilder
{
    readonly Mesh _mesh;
    readonly Vector3[] _verts;
    readonly int[] _indices;

    public TubeMeshBuilder(int maxPoints, int radialSegments)
    {
        _mesh = new Mesh { name = "Tube" };
        _mesh.MarkDynamic(); // tells Unity the buffers change frequently — avoids reallocations
        _verts   = new Vector3[maxPoints * radialSegments];
        _indices = new int[(maxPoints - 1) * radialSegments * 6];
    }

    // Rebuild for `count` path points; only the used range is uploaded.
    public Mesh Build(IReadOnlyList<Vector3> path, int count, float radius, int radialSegments)
    {
        // Parallel-transport frame: carry the previous ring's normal forward instead of
        // recomputing from an arbitrary up-vector, so the tube does not TWIST along bends.
        Vector3 normal = ComputeInitialNormal(path[0], path[1]);
        int vi = 0, ii = 0;
        for (int p = 0; p < count; p++)
        {
            Vector3 tangent = Tangent(path, p, count);
            normal = Vector3.ProjectOnPlane(normal, tangent).normalized; // re-orthogonalize, no recompute
            Vector3 binormal = Vector3.Cross(tangent, normal);
            for (int s = 0; s < radialSegments; s++)
            {
                float a = (float)s / radialSegments * Mathf.PI * 2f;
                _verts[vi++] = path[p] + (Mathf.Cos(a) * normal + Mathf.Sin(a) * binormal) * radius;
            }
            // ... fill _indices for the quad strip between ring p-1 and p ...
        }

        // Bounded overloads upload ONLY the used range — no per-frame GC and no stale tail.
        _mesh.SetVertices(_verts, 0, vi);
        _mesh.SetIndices(_indices, 0, ii, MeshTopology.Triangles, 0);
        _mesh.RecalculateBounds();
        return _mesh;
    }
}
```

**Per-instance texture without material instancing.** When several tubes share one material but need different textures (e.g. team-colored ropes), override `_BaseMap` via a `MaterialPropertyBlock` on the renderer — this stays on the SRP-batcher fast path and creates no material instances:

```csharp
var mpb = new MaterialPropertyBlock();
mpb.SetTexture("_BaseMap", instanceTexture);
meshRenderer.SetPropertyBlock(mpb);
```

**Key points**
- `MarkDynamic()` once at creation; reuse the same `Mesh` + arrays every frame.
- Use the bounded `SetVertices(array, start, length)` / `SetIndices(array, start, length, ...)` overloads so a shrinking path does not leave a stale tail and no new array is allocated per frame.
- Mitered rings + a **parallel-transport frame** (carry the previous normal forward, re-orthogonalize against the new tangent) avoid the twist you get from re-deriving the frame from a fixed up-vector at each point.
- Per-instance texture → `MaterialPropertyBlock` `_BaseMap`, never a new material.
