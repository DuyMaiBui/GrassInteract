#nullable enable
using System.Collections.Generic;
using GrassInteract;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Handles all scene-view mouse input for Instance scatter layer authoring.
    ///
    /// Registered via [InitializeOnLoad] — subscribes to SceneView.duringSceneGui once per
    /// editor session and routes per-mode behaviour:
    ///
    ///   Select mode — click → find nearest record (sphere pick), dispatch to RecordList +
    ///                 draw TRS gizmo via InstanceSelectionOverlay.
    ///   Place mode  — LMB click or Shift-drag → raycast ground, ghost preview, add record.
    ///   Erase mode  — draw brush disc, LMB click/drag → remove records inside radius.
    ///
    /// Erase throttle: hover-list rebuilt at 10 Hz maximum to avoid stall on large scenes.
    /// </summary>
    [InitializeOnLoad]
    internal static class InstanceSceneInput
    {
        // ── Registration ──────────────────────────────────────────────────────

        static InstanceSceneInput()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        // ── State ─────────────────────────────────────────────────────────────

        private static readonly InstancePickingService pickingService = new();
        private static InstanceScatterLayer? lastKnownLayer;

        private static Vector3 lastPlacedPos = Vector3.positiveInfinity;
        private static bool isPlaceDragging;
        private static bool didPlaceStroke;
        private static int placeControlId;
        private static int placeUndoGroup = -1;

        private static readonly List<int> eraseHoverIndices = new();
        private static double lastEraseHoverTime;
        private static bool didEraseStroke;
        private static int eraseUndoGroup = -1;
        private const double ERASE_HOVER_INTERVAL = 0.1;

        private static bool hasPendingPickingRebuild;
        private static InstanceScatterLayer? pendingRebuildLayer;
        private static AuthoredInstancesData? pendingRebuildSidecar;

        private static Material? ghostMat;

        // ── Scene GUI entry point ─────────────────────────────────────────────

        private static void OnSceneGui(SceneView sceneView)
        {
            InstanceScatterLayer? layer = InstancePlacementOverlay.ActiveLayer;
            if (layer == null)
                return;

            var sidecar = layer.AuthoredInstances;
            if (sidecar == null)
                return;

            if (layer != lastKnownLayer)
            {
                lastKnownLayer = layer;
                pickingService.Rebuild(sidecar, layer);
            }

            TryFlushPendingPickingRebuild(layer, sidecar);

            switch (InstancePlacementOverlay.Mode)
            {
                case InstanceEditMode.Select:
                    HandleSelectMode(sceneView, layer, sidecar);
                    break;
                case InstanceEditMode.Place:
                    HandlePlaceMode(sceneView, layer, sidecar);
                    break;
                case InstanceEditMode.Erase:
                    HandleEraseMode(sceneView, layer, sidecar);
                    break;
            }
        }

        // ── Select mode ───────────────────────────────────────────────────────

        private static void HandleSelectMode(SceneView sceneView, InstanceScatterLayer layer, AuthoredInstancesData sidecar)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                InstancePlacementOverlay.ExitSceneMode();
                e.Use();
                return;
            }

            InstanceSelectionOverlay.OnSceneGUI(layer, sidecar);

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.shift && !e.control)
            {
                if (!pickingService.IsValid)
                    pickingService.Rebuild(sidecar, layer);

                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                float bestT = float.MaxValue;
                int? hitIdx = pickingService.RaycastPick(ray, layer, sidecar, ref bestT);

                if (hitIdx.HasValue)
                {
                    InstanceSelectionService.Select(layer, hitIdx.Value);
                    DispatchToRecordList(hitIdx.Value);
                    InstancePlacementOverlay.RefreshToolbarStatus();
                }
                else
                {
                    InstanceSelectionService.ClearRecordSelection();
                }

                e.Use();
                sceneView.Repaint();
            }
        }

        // ── Place mode ────────────────────────────────────────────────────────

        private static void HandlePlaceMode(SceneView sceneView, InstanceScatterLayer layer, AuthoredInstancesData sidecar)
        {
            Event e = Event.current;

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.W ||
                                                 e.keyCode == KeyCode.E ||
                                                 e.keyCode == KeyCode.R))
            {
                e.Use();
                return;
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                InstancePlacementOverlay.ExitSceneMode();
                e.Use();
                return;
            }

            bool needsRaycast = e.type == EventType.MouseDown
                             || e.type == EventType.MouseDrag
                             || e.type == EventType.MouseUp
                             || e.type == EventType.Repaint;

            RaycastHit groundHit = default;
            bool hit = false;
            if (needsRaycast)
            {
                Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                hit = TryRaycastGround(mouseRay, layer, out groundHit);
            }

            if (hit && e.type == EventType.Repaint)
            {
                DrawGhostPreview(layer, groundHit.point, groundHit.normal);
                Handles.color = new Color(0.3f, 1f, 0.3f, 0.6f);
                Handles.DrawWireDisc(groundHit.point, groundHit.normal, layer.PlaceSpacing * 0.5f);
            }

            bool consumed = false;

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (hit)
                {
                    BeginStrokeUndo(sidecar, "Place Instances", ref placeUndoGroup);
                    PlaceRecord(layer, sidecar, groundHit);
                    didPlaceStroke = true;
                    isPlaceDragging = e.shift;
                    consumed = true;
                }
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && isPlaceDragging && hit)
            {
                float spacing = Mathf.Max(0.05f, layer.PlaceSpacing);
                float dist = Vector3.Distance(new Vector3(lastPlacedPos.x, groundHit.point.y, lastPlacedPos.z),
                                               groundHit.point);
                if (dist >= spacing)
                {
                    if (!didPlaceStroke)
                        BeginStrokeUndo(sidecar, "Place Instances", ref placeUndoGroup);

                    PlaceRecord(layer, sidecar, groundHit);
                    didPlaceStroke = true;
                    consumed = true;
                }
                else
                {
                    if (placeControlId == 0)
                        placeControlId = GUIUtility.GetControlID(FocusType.Passive);
                    GUIUtility.hotControl = placeControlId;
                    consumed = true;
                }
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                isPlaceDragging = false;
                lastPlacedPos = Vector3.positiveInfinity;
                placeControlId = 0;

                if (didPlaceStroke)
                {
                    EndStrokeUndo(ref placeUndoGroup);
                    TryRequestPickingRebuild(layer, sidecar, rebuildImmediately: true);
                    InstancePlacementOverlay.RefreshToolbarStatus();
                    EditorUtility.SetDirty(sidecar);
                    consumed = true;
                }

                didPlaceStroke = false;
            }

            if (e.type == EventType.Layout)
            {
                if (placeControlId == 0)
                    placeControlId = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(placeControlId);
            }

            if (consumed)
                e.Use();

            if (consumed || e.type == EventType.Repaint)
                sceneView.Repaint();
        }

        private static void PlaceRecord(InstanceScatterLayer layer, AuthoredInstancesData sidecar,
                                         RaycastHit hit)
        {
            Quaternion rot;
            if (InstancePlacementOverlay.SnapAlignNormal)
                rot = Quaternion.FromToRotation(Vector3.up, hit.normal);
            else
                rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var record = new InstanceRecord
            {
                position = hit.point,
                rotation = rot,
                scale = 1f,
                overrideMask = InstanceOverrideMask.None,
            };

            sidecar.AddRecord(record);
            sidecar.PackBlob();
            lastPlacedPos = hit.point;

            TryRequestPickingRebuild(layer, sidecar, rebuildImmediately: false);
            EditorUtility.SetDirty(sidecar);
        }

        // ── Erase mode ────────────────────────────────────────────────────────

        private static int eraseControlId;

        private static void HandleEraseMode(SceneView sceneView, InstanceScatterLayer layer, AuthoredInstancesData sidecar)
        {
            Event e = Event.current;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                InstancePlacementOverlay.ExitSceneMode();
                e.Use();
                return;
            }

            bool needsRaycast = e.type == EventType.MouseDown
                             || e.type == EventType.MouseDrag
                             || e.type == EventType.MouseUp
                             || e.type == EventType.Repaint;

            RaycastHit groundHit = default;
            bool hit = false;
            if (needsRaycast)
            {
                Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                hit = TryRaycastGround(mouseRay, layer, out groundHit);
            }

            float radius = InstancePlacementOverlay.EraseBrushRadius;
            bool consumed = false;

            if (hit && EditorApplication.timeSinceStartup - lastEraseHoverTime >= ERASE_HOVER_INTERVAL)
            {
                lastEraseHoverTime = EditorApplication.timeSinceStartup;
                if (!pickingService.IsValid)
                    pickingService.Rebuild(sidecar, layer);
                eraseHoverIndices.Clear();
                eraseHoverIndices.AddRange(pickingService.QueryRadius(groundHit.point, radius));
            }

            if (hit && e.type == EventType.Repaint)
            {
                Handles.color = new Color(1f, 0.25f, 0.25f, 0.7f);
                Handles.DrawWireDisc(groundHit.point, Vector3.up, radius);

                Handles.color = new Color(1f, 0.2f, 0.2f, 0.5f);
                var workList = sidecar.WorkingList;
                foreach (int idx in eraseHoverIndices)
                {
                    if (idx < 0 || idx >= workList.Count)
                        continue;

                    Handles.DrawSolidDisc(workList[idx].position, Vector3.up, 0.15f);
                }
            }

            bool isMousePress = e.type == EventType.MouseDown && e.button == 0 && !e.alt;
            bool isMouseDrag = e.type == EventType.MouseDrag && e.button == 0;
            if ((isMousePress || isMouseDrag) && hit && eraseHoverIndices.Count > 0)
            {
                if (!didEraseStroke)
                    BeginStrokeUndo(sidecar, "Erase Instances", ref eraseUndoGroup);

                eraseHoverIndices.Sort((a, b) => b.CompareTo(a));
                foreach (int idx in eraseHoverIndices)
                {
                    if (idx >= 0 && idx < sidecar.WorkingList.Count)
                        sidecar.RemoveRecordSwapPop(idx);
                }
                eraseHoverIndices.Clear();

                didEraseStroke = true;
                sidecar.PackBlob();
                EditorUtility.SetDirty(sidecar);
                TryRequestPickingRebuild(layer, sidecar, rebuildImmediately: false);
                InstanceSelectionService.ClearRecordSelection();
                InstancePlacementOverlay.RefreshToolbarStatus();
                consumed = true;
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (didEraseStroke)
                {
                    EndStrokeUndo(ref eraseUndoGroup);
                    EditorUtility.SetDirty(sidecar);
                    TryRequestPickingRebuild(layer, sidecar, rebuildImmediately: true);
                    consumed = true;
                }

                didEraseStroke = false;
            }

            if (e.type == EventType.Layout)
            {
                if (eraseControlId == 0)
                    eraseControlId = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(eraseControlId);
            }

            if (consumed)
                e.Use();

            if (consumed || e.type == EventType.Repaint)
                sceneView.Repaint();
        }

        private static void BeginStrokeUndo(AuthoredInstancesData sidecar, string actionName, ref int undoGroup)
        {
            if (undoGroup >= 0)
                return;

            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(actionName);
            Undo.RecordObject(sidecar, actionName);
        }

        private static void EndStrokeUndo(ref int undoGroup)
        {
            if (undoGroup < 0)
                return;

            Undo.CollapseUndoOperations(undoGroup);
            undoGroup = -1;
        }

        internal static void TryRequestPickingRebuild(InstanceScatterLayer layer, AuthoredInstancesData sidecar, bool rebuildImmediately)
        {
            eraseHoverIndices.Clear();
            lastEraseHoverTime = 0d;

            if (rebuildImmediately)
            {
                hasPendingPickingRebuild = false;
                pendingRebuildLayer = null;
                pendingRebuildSidecar = null;
                lastKnownLayer = layer;
                pickingService.Rebuild(sidecar, layer);
                return;
            }

            hasPendingPickingRebuild = true;
            pendingRebuildLayer = layer;
            pendingRebuildSidecar = sidecar;
            pickingService.Invalidate();
        }

        private static void TryFlushPendingPickingRebuild(InstanceScatterLayer layer, AuthoredInstancesData sidecar)
        {
            if (!hasPendingPickingRebuild)
                return;

            if (pendingRebuildLayer != layer || pendingRebuildSidecar != sidecar)
                return;

            Event e = Event.current;
            if (e.type != EventType.MouseUp)
                return;

            hasPendingPickingRebuild = false;
            pendingRebuildLayer = null;
            pendingRebuildSidecar = null;
            lastKnownLayer = layer;
            pickingService.Rebuild(sidecar, layer);
        }

        // ── Ghost preview ─────────────────────────────────────────────────────

        private static void DrawGhostPreview(InstanceScatterLayer layer, Vector3 pos, Vector3 normal)
        {
            if (layer.LodMeshes == null || layer.LodMeshes.Length == 0) return;
            Mesh? mesh = layer.LodMeshes[0];
            if (mesh == null) return;

            Quaternion rot = InstancePlacementOverlay.SnapAlignNormal
                ? Quaternion.FromToRotation(Vector3.up, normal)
                : Quaternion.identity;

            if (ghostMat == null)
            {
                ghostMat = new Material(Shader.Find("Standard"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                ghostMat.color = new Color(0.3f, 1f, 0.3f, 0.35f);
                ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                ghostMat.SetInt("_ZWrite", 0);
                ghostMat.renderQueue = 3000;
            }

            if (ghostMat.SetPass(0))
                Graphics.DrawMeshNow(mesh, Matrix4x4.TRS(pos, rot, Vector3.one));
        }

        // ── Ground raycast ────────────────────────────────────────────────────

        private static bool TryRaycastGround(Ray ray, InstanceScatterLayer layer, out RaycastHit hit)
        {
            int mask = layer.GroundSnapMask;
            if (mask == 0)
                mask = ~0;

            return Physics.Raycast(ray, out hit, 2000f, mask, QueryTriggerInteraction.Ignore);
        }

        // ── RecordList dispatch ───────────────────────────────────────────────

        private static void DispatchToRecordList(int idx)
        {
            // InstanceSelectionService.Select() called in HandleSelectMode fires the shared
            // selection state used by the authoring UI and any future record list binding.
        }
    }
}
