#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Draws wireframe + transform gizmos for the currently selected authored instance.
    /// Selection state comes from <see cref="InstanceSelectionService"/>.
    /// </summary>
    internal static class InstanceSelectionOverlay
    {
        internal static void OnSceneGUI(InstanceScatterLayer layer, AuthoredInstancesData sidecar)
        {
            if (InstanceSelectionService.CurrentLayer != layer)
                return;

            int idx = InstanceSelectionService.SelectedRecordIndex;
            if (idx < 0)
                return;

            if (!sidecar.TryGetRecord(idx, out InstanceRecord rec))
            {
                InstanceSelectionService.ClearRecordSelection();
                return;
            }

            Mesh[]? meshes = layer.LodMeshes;
            if (meshes == null || meshes.Length == 0)
                return;

            Mesh? mesh = meshes[0];
            if (mesh == null)
                return;

            Handles.color = new Color(0.4f, 1f, 1f, 0.7f);
            Bounds localBounds = mesh.bounds;
            Matrix4x4 prevMatrix = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(rec.position, rec.rotation, Vector3.one * (rec.scale * 1.02f));
            Handles.DrawWireCube(localBounds.center, localBounds.size);
            Handles.matrix = prevMatrix;

            Tool activeTool = Tools.current;

            if (activeTool == Tool.Move)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(rec.position, rec.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(sidecar, "Move Instance");
                    rec.position = newPos;
                    sidecar.SetRecord(idx, rec);
                    sidecar.PackBlob();
                    InstanceSceneInput.TryRequestPickingRebuild(layer, sidecar, rebuildImmediately: Event.current.type == EventType.MouseUp);
                    EditorUtility.SetDirty(sidecar);
                }
            }
            else if (activeTool == Tool.Rotate)
            {
                EditorGUI.BeginChangeCheck();
                Quaternion newRot = Handles.RotationHandle(rec.rotation, rec.position);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(sidecar, "Rotate Instance");
                    rec.rotation = newRot;
                    sidecar.SetRecord(idx, rec);
                    sidecar.PackBlob();
                    InstanceSceneInput.TryRequestPickingRebuild(layer, sidecar, rebuildImmediately: Event.current.type == EventType.MouseUp);
                    EditorUtility.SetDirty(sidecar);
                }
            }
            else if (activeTool == Tool.Scale)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 scaleV3 = new Vector3(rec.scale, rec.scale, rec.scale);
                Vector3 newScaleV3 = Handles.ScaleHandle(scaleV3, rec.position, rec.rotation, HandleUtility.GetHandleSize(rec.position));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(sidecar, "Scale Instance");
                    rec.scale = (newScaleV3.x + newScaleV3.y + newScaleV3.z) / 3f;
                    sidecar.SetRecord(idx, rec);
                    sidecar.PackBlob();
                    InstanceSceneInput.TryRequestPickingRebuild(layer, sidecar, rebuildImmediately: Event.current.type == EventType.MouseUp);
                    EditorUtility.SetDirty(sidecar);
                }
            }
        }
    }
}
