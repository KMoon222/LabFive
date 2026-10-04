using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AvoiderDLLUnity
{

    public class Avoider : MonoBehaviour
    {
        // variables in inspector
        public GameObject ObjectToAvoid;
        public float AvoiderSpeed = 5f;
        public float AvoiderRange = 4f;
        public bool GizmosToggle = true;
        public float rad = 1;
        public LayerMask walls;

        // private variables for logic use, and lists to store points in 
        private bool seenByPlayer = false;
        private List<Vector3> drawSamples = new List<Vector3>();
        private List<Vector3> hiddenSamples = new List<Vector3>();
        private bool noTarget = true;
        private Vector3 lastPoint;

        public void Update()
        {
            // check if avoider is seen by the player 
            seenByPlayer = SeenByPlayer(transform.position);

            // if true, create a poisson disc sampler, and add the ones hidden from the player to a list (hiddenSamples)
            if (seenByPlayer)
            {
                if (noTarget == true)
                {
                    drawSamples.Clear();
                    hiddenSamples.Clear();
                    var sampler = new PoissonDiscSampler(transform.position, AvoiderRange, rad);
                    foreach (var point in sampler.Samples())
                    {
                        drawSamples.Add(point);
                        if (SeenByPlayer(point) == false && IsPointOnNavMesh(point, AvoiderRange) && BehindWall(point) == false)
                        {
                            hiddenSamples.Add(point);
                        }
                    }

                    // find the closest hidden point and set as target
                    var lastDistance = 1000f;
                    foreach (var point in hiddenSamples)
                    {
                        var distance = (point - ObjectToAvoid.transform.position).sqrMagnitude;
                        if (distance < lastDistance)
                        {
                            lastDistance = distance;
                            lastPoint = point;
                        }
                    }
                    noTarget = false;
                }
            }

            // move avoider towards target point
            if (noTarget == false)
            {
                transform.position = Vector3.MoveTowards(transform.position, lastPoint, AvoiderSpeed * Time.deltaTime);
                if (Vector3.Distance(transform.position, lastPoint) <= 1f)
                {
                    noTarget = true;
                    seenByPlayer = false;
                }
            }
        }

        // method to check if a point is on the NavMesh
        public bool IsPointOnNavMesh(Vector3 targetPoint, float maxDistance)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(targetPoint, out hit, 1f, NavMesh.AllAreas))
            {
                return true;
            }
            return false;
        }

        // method to check if a point can be seen by the player
        private bool SeenByPlayer(Vector3 point)
        {
            if (Physics.Raycast(point, ObjectToAvoid.transform.position - point, out RaycastHit hit, AvoiderRange, walls))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        // method to check if a point is behind a wall, and shouldn't be in hiddenSamples 
        private bool BehindWall(Vector3 point)
        {
            if (Physics.Raycast(point, transform.position - point, out RaycastHit hit, AvoiderRange, walls))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // draw the gizmos in the editor if the toggle is on 
        private void OnDrawGizmos()
        {
            if (GizmosToggle)
            {
                foreach (var point in drawSamples)
                {
                    if (SeenByPlayer(point) == false && IsPointOnNavMesh(point, AvoiderRange) && BehindWall(point) == false)
                    {
                        Gizmos.color = Color.green;
                    }
                    else
                    {
                        Gizmos.color = Color.red;
                    }
                    Gizmos.DrawLine(transform.position, point);
                }
            }
        }
    }



    // poisson disc sampler script with adjustments to work on the xz plane, locally to avoider object
    public class PoissonDiscSampler
    {
        private const int k = 30;

        private Vector3 center;
        private Rect rect;
        private float radius2;
        private float cellSize;
        private Vector3[,] grid;
        private List<Vector3> activeSamples = new List<Vector3>();

        public PoissonDiscSampler(Vector3 center, float areaRadius, float minDistance)
        {
            this.center = center;
            radius2 = minDistance * minDistance;
            cellSize = minDistance / Mathf.Sqrt(2f);

            rect = new Rect(center.x - areaRadius, center.z - areaRadius, areaRadius * 2f, areaRadius * 2f);

            int gx = Mathf.Max(1, Mathf.CeilToInt(rect.width / cellSize));
            int gz = Mathf.Max(1, Mathf.CeilToInt(rect.height / cellSize));
            grid = new Vector3[gx, gz];
        }

        public IEnumerable<Vector3> Samples()
        {
            Vector2 firstOffset = UnityEngine.Random.insideUnitCircle * (rect.width * 0.5f);
            Vector3 firstSample = new Vector3(center.x + firstOffset.x, center.y, center.z + firstOffset.y);

            if (IsInsideDisc(firstSample))
                yield return AddSample(firstSample);

            while (activeSamples.Count > 0)
            {
                int i = (int)(UnityEngine.Random.value * activeSamples.Count);
                Vector3 sample = activeSamples[i];

                bool found = false;
                for (int j = 0; j < k; ++j)
                {
                    float angle = 2f * Mathf.PI * UnityEngine.Random.value;
                    float r = Mathf.Sqrt(UnityEngine.Random.value * 3f * radius2 + radius2);
                    Vector3 candidate = sample + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);

                    if (IsInsideRect(candidate) && IsInsideDisc(candidate) && IsFarEnough(candidate))
                    {
                        found = true;
                        yield return AddSample(candidate);
                        break;
                    }
                }

                if (!found)
                {
                    // remove sample i
                    activeSamples[i] = activeSamples[activeSamples.Count - 1];
                    activeSamples.RemoveAt(activeSamples.Count - 1);
                }
            }
        }

        private bool IsInsideRect(Vector3 sample)
        {
            return rect.Contains(new Vector2(sample.x, sample.z));
        }

        private bool IsInsideDisc(Vector3 sample)
        {
            Vector2 offset = new Vector2(sample.x - center.x, sample.z - center.z);
            return offset.sqrMagnitude <= (rect.width * 0.5f) * (rect.width * 0.5f);
        }

        private bool IsFarEnough(Vector3 sample)
        {
            GridPos pos = new GridPos(sample, rect, cellSize);

            int xmin = Mathf.Max(pos.x - 2, 0);
            int zmin = Mathf.Max(pos.z - 2, 0);
            int xmax = Mathf.Min(pos.x + 2, grid.GetLength(0) - 1);
            int zmax = Mathf.Min(pos.z + 2, grid.GetLength(1) - 1);

            for (int z = zmin; z <= zmax; z++)
            {
                for (int x = xmin; x <= xmax; x++)
                {
                    Vector3 s = grid[x, z];
                    if (s != Vector3.zero)
                    {
                        Vector3 d = s - sample;
                        if (d.x * d.x + d.z * d.z < radius2) return false;
                    }
                }
            }

            return true;
        }

        private Vector3 AddSample(Vector3 sample)
        {
            activeSamples.Add(sample);
            GridPos pos = new GridPos(sample, rect, cellSize);
            grid[pos.x, pos.z] = sample;
            return sample;
        }

        private struct GridPos
        {
            public int x;
            public int z;

            public GridPos(Vector3 sample, Rect rect, float cellSize)
            {
                x = Mathf.Clamp((int)((sample.x - rect.x) / cellSize), 0, Mathf.Max(0, Mathf.CeilToInt(rect.width / cellSize) - 1));
                z = Mathf.Clamp((int)((sample.z - rect.y) / cellSize), 0, Mathf.Max(0, Mathf.CeilToInt(rect.height / cellSize) - 1));
            }
        }
    }


    // warning messages pop up if NavMesh is not assigned or baked, or if there is no object to avoid assigned in inspector
#if UNITY_EDITOR
public class AvoiderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        NavMeshAgent navmesh = ((Avoider)target).GetComponent<NavMeshAgent>();
        if (navmesh == null)
        {
            EditorGUILayout.HelpBox("This object needs to have a NavMesh Agent and a baked NavMesh", MessageType.Warning);
        }
        if (((Avoider)target).ObjectToAvoid == null)
        {
            EditorGUILayout.HelpBox("This object must be assigned an object to avoid", MessageType.Warning);

        }
    }
}
#endif

}
