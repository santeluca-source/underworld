using System.Text;
using UnityEditor;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Diagnostica dei modelli Kenney: gerarchia, misure, materiali, uso della colormap.
    /// Riga di comando: -executeMethod Underworld.EditorTools.WorldArtDiagnostics.Dump
    /// </summary>
    public static class WorldArtDiagnostics
    {
        static readonly string[] Samples =
        {
            "CarKit/sedan.fbx", "CarKit/police.fbx", "CarKit/van.fbx", "CarKit/cone.fbx", "CarKit/taxi.fbx",
            "CarKit/suv.fbx", "CarKit/hatchback-sports.fbx", "CarKit/box.fbx",
            "FactoryKit/box-small.fbx", "FactoryKit/warning-traffic.fbx", "FactoryKit/cone.fbx",
            "NatureKit/tree_oak.fbx", "NatureKit/tree_default.fbx", "NatureKit/plant_bush.fbx", "NatureKit/flower_redA.fbx",
            "NatureKit/pot_large.fbx",
        };

        public static void Dump()
        {
            var sb = new StringBuilder();
            foreach (var rel in Samples)
            {
                string path = KenneyPaths.Root + rel;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { sb.AppendLine($"[ART] {rel}: NON TROVATO"); continue; }
                sb.AppendLine($"[ART] {rel}: root pos={go.transform.localPosition} rot={go.transform.localEulerAngles} scale={go.transform.localScale}");
                var b = new Bounds();
                bool has = false;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    var m = mf.transform.localToWorldMatrix;
                    sb.AppendLine($"   part '{mf.name}' mesh={mesh?.name} v={mesh?.vertexCount} sub={mesh?.subMeshCount} readable={mesh?.isReadable} lossyScale={mf.transform.lossyScale}");
                    if (mr != null)
                        foreach (var mat in mr.sharedMaterials)
                            sb.AppendLine($"      mat '{mat?.name}' shader={mat?.shader?.name} color={(mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "-")} tex={(mat != null && mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")?.name : "-")}");
                    if (mesh == null) continue;
                    var v = mesh.vertices;
                    foreach (var p in v)
                    {
                        var w = m.MultiplyPoint3x4(p);
                        if (!has) { b = new Bounds(w, Vector3.zero); has = true; } else b.Encapsulate(w);
                    }
                    var uv = mesh.uv;
                    if (uv != null && uv.Length > 0)
                    {
                        var counts = new int[8, 4];
                        float minEdge = 1f;
                        foreach (var t in uv)
                        {
                            int px = Mathf.Clamp(Mathf.FloorToInt(t.x * 8f), 0, 7);
                            int row = Mathf.Clamp(Mathf.FloorToInt((1f - t.y) * 4f), 0, 3);
                            counts[px, row]++;
                            float fx = t.x * 8f - Mathf.Floor(t.x * 8f);
                            minEdge = Mathf.Min(minEdge, Mathf.Min(fx, 1f - fx));
                        }
                        var line = new StringBuilder("      uv pairs:");
                        for (int r = 0; r < 4; r++) for (int px = 0; px < 8; px++) if (counts[px, r] > 0) line.Append($" r{r}p{px}={counts[px, r]}");
                        line.Append($" minEdgeFrac={minEdge:0.000}");
                        sb.AppendLine(line.ToString());
                    }
                }
                sb.AppendLine($"   bounds center={b.center} size={b.size}");
            }
            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
