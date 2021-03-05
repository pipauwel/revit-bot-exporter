using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;

namespace TUe.ISBE.LBDExporter
{
    class Util
    {
        private static int objStartIndex = 0;
        private static int objStartNormal = 0;

        public static string TypeNameToId(string Name)
        {
            string id = Name.Replace("(", "").Replace(")", "").Replace(" ", "_");

            id = System.Uri.EscapeDataString(id);

            return id;
        }

        public static string CreateURI(Element e, string Namespace)
        {

            string cat = e.Category.Name;
            string elType = cat.ToLower();   // Make lower case
            if(elType.EndsWith("s"))
                elType = elType.Remove(cat.Length - 1); // Singularize

            string guid = System.Uri.EscapeDataString(e.GetUUID());
            guid = guid.Replace("$", "_");
            string uri = $"{Namespace}{elType}_{ guid }"; //Namespace needs to include hash or slash 
            //string uri = $"{Host}/{ProNum}/{elType}_{ e.UniqueId }";

            uri = uri.Replace(" ", "_");

            return uri;
        }
        

        public static string GetWKTLine(Element e)
        {
            String stOut = "";

            // Get element geometry
            Options opt = new Options();
            GeometryElement geomElem = e.get_Geometry(opt);     
            BoundingBoxXYZ box = geomElem.GetBoundingBox();

            // Note that the section box can be rotated and transformed.  
            // So the min/max corners coordinates relative to the model must be computed via the transform.
            Transform trf = box.Transform;

            XYZ max = box.Max; //Maximum coordinates (upper-right-front corner of the box before transform is applied).
            XYZ min = box.Min; //Minimum coordinates (lower-left-rear corner of the box before transform is applied).

            // Transform the min and max to model coordinates
            XYZ maxInModelCoords = trf.OfPoint(max);
            XYZ minInModelCoords = trf.OfPoint(min);

            stOut += "(" + (minInModelCoords.X * 12 * 25.4).ToString().Replace(',', '.') + " "
                + (minInModelCoords.Y * 12 * 25.4).ToString().Replace(',', '.') + ", "
                + (minInModelCoords.X * 12 * 25.4).ToString().Replace(',', '.') + " "
                + (maxInModelCoords.Y * 12 * 25.4).ToString().Replace(',', '.') + ")";

                /*+(boundarySegment.GetCurve().GetEndPoint(0).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                + (boundarySegment.GetCurve().GetEndPoint(0).Y * 12 * 25.4).ToString().Replace(',', '.') + ", "
                + (boundarySegment.GetCurve().GetEndPoint(1).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                + (boundarySegment.GetCurve().GetEndPoint(1).Y * 12 * 25.4).ToString().Replace(',', '.') + ")\" ."*/

                return stOut;
        }

        public static string GetFacesAndEdges(Element e, bool startFromZero)
        {
            String xx = "";
            bool RetainCurvedSurfaceFacets = true;

            // Get element geometry
            Options opt = new Options();
            GeometryElement geomElem = e.get_Geometry(opt);

            int[] triangleIndices = new int[3];
            XYZ[] triangleCorners = new XYZ[3];
            List<string> faceVertices = new List<string>();
            List<string> faceNormals = new List<string>();
            List<string> faceElements = new List<string>();

            //// First we need to get transformation
            //LocationCurve lc = e.Location as LocationCurve;

            //// Get curve starting- and endpoint
            //XYZ startingPoint = lc.Curve.GetEndPoint(0);
            //XYZ endPoint = lc.Curve.GetEndPoint(1);

            foreach (GeometryObject geomObj in geomElem)
            {
                Solid geomSolid = geomObj as Solid;
                if (null != geomSolid)
                {

                    faceVertices.Clear();
                    faceNormals.Clear();
                    faceElements.Clear();

                    foreach (Face face in geomSolid.Faces)
                    {
                        // Triangulate face to get mesh
                        Mesh mesh = face.Triangulate();

                        int nTriangles = mesh.NumTriangles;

                        IList<XYZ> vertices = mesh.Vertices;

                        int nVertices = vertices.Count;

                        List<int> vertexCoordsMm = new List<int>(3 * nVertices);

                        // A vertex may be reused several times with 
                        // different normals for different faces, so 
                        // we cannot precalculate normals per vertex.
                        // List<double> normals = new List<double>( 3 * nVertices );

                        // Extract vertices
                        foreach (XYZ v in vertices)
                        {
                            vertexCoordsMm.Add(ConvertLengthToMM(v.X));
                            vertexCoordsMm.Add(ConvertLengthToMM(v.Y));
                            vertexCoordsMm.Add(ConvertLengthToMM(v.Z));
                        }

                        // Loop over triangles
                        for (int i = 0; i < nTriangles; ++i)
                        {
                            MeshTriangle triangle = mesh.get_Triangle(i);

                            for (int j = 0; j < 3; ++j)
                            {
                                int k = (int)triangle.get_Index(j);
                                triangleIndices[j] = k;
                                triangleCorners[j] = vertices[k];
                            }

                            // Calculate constant triangle facet normal.
                            XYZ v = triangleCorners[1]
                              - triangleCorners[0];
                            XYZ w = triangleCorners[2]
                              - triangleCorners[0];
                            XYZ triangleNormal = v
                              .CrossProduct(w)
                              .Normalize();

                            // List to store vertice indexes in the form: [v1//vn1 v2//vn2 v3//vn3]
                            List<string> vertIndexes = new List<string>();

                            for (int j = 0; j < 3; ++j)
                            {
                                int nFaceVertices = faceVertices.Count;

                                //if(nFaceVertices != faceNormals.Count)
                                //{
                                //    xx += "expected equal number of face vertex and normal coordinates\n";
                                //}

                                int i3 = triangleIndices[j] * 3;

                                // Rotate the X, Y and Z directions, 
                                // since the Z direction points upward
                                // in Revit as opposed to sideways or
                                // outwards or forwards in WebGL.

                                string vStr = $"v {vertexCoordsMm[i3]} {vertexCoordsMm[i3 + 1]} {vertexCoordsMm[i3 + 2]}";

                                // get vertice index
                                int vidx = faceVertices.IndexOf(vStr);

                                // add if not exist
                                if (vidx == -1)
                                {
                                    faceVertices.Add(vStr);
                                    vidx = faceVertices.Count-1;
                                }
                                    

                                string vnStr = "";
                                if (RetainCurvedSurfaceFacets)
                                {
                                    vnStr = $"vn {Math.Round(triangleNormal.X, 2)} {Math.Round(triangleNormal.Y, 2)} {Math.Round(triangleNormal.Z, 2)}";
                                }
                                else
                                {
                                    UV uv = face.Project(
                                      triangleCorners[j]).UVPoint;

                                    XYZ normal = face.ComputeNormal(uv);

                                    vnStr = $"vn {Math.Round(normal.X, 2)} {Math.Round(normal.Y, 2)} {Math.Round(normal.Z, 2)}";
                                }

                                // get face normal index
                                int vnidx = faceNormals.IndexOf(vnStr);

                                // add if not in list
                                if(vnidx == -1)
                                {
                                    faceNormals.Add(vnStr);
                                    vnidx = faceNormals.Count - 1;
                                }

                                // add indexes to list
                                vertIndexes.Add($"{vidx+1+objStartIndex}/{vnidx+1 + objStartNormal}");

                            }

                            // Store face elements
                            string fStr = $"f {vertIndexes[0]} {vertIndexes[1]} {vertIndexes[2]}";
                            faceElements.Add(fStr);
                        }

                    }

                    // Write to string
                    xx += String.Join("\n\t", faceVertices) + "\n\t";
                    xx += String.Join("\n\t", faceNormals) + "\n\t";
                    xx += String.Join("\n\t", faceElements) + "\n\t";
                    if (!startFromZero)
                    {
                        objStartIndex += faceVertices.Count;
                        objStartNormal += faceNormals.Count;
                    }

                }

                Mesh geomMesh = geomObj as Mesh;
                Curve geomCurve = geomObj as Curve;
                Point geomPoint = geomObj as Point;
                PolyLine geomPoly = geomObj as PolyLine;

                GeometryInstance geomInst = geomObj as GeometryInstance;
                if (null != geomInst)
                {
                    GeometryElement geomElement = geomInst.GetInstanceGeometry();
                    foreach (GeometryObject geomObj1 in geomElement)
                    {
                        Solid geomSolid1 = geomObj1 as Solid;
                        if (null != geomSolid1)
                        {
                            Console.Out.WriteLine("got element: " + geomSolid1.Faces);
                        }
                    }
                }
            }          

            return xx;
        }

        public static int ConvertLengthToMM(Double len)
        {
            return Convert.ToInt32(Math.Round(UnitUtils.ConvertFromInternalUnits(len, Autodesk.Revit.DB.DisplayUnitType.DUT_MILLIMETERS)));
        }
        
    }

    static class Extensions
    {
        /// <summary>
        /// Get internal UUID for element
        /// </summary>
        /// <param name="e">Revit Element</param>
        /// <returns>String UUID</returns>
        public static string GetUUID(this Element e)
        {
            return e.UniqueId;
        }

        /// <summary>
        /// Get IFC GUID for element
        /// </summary>
        /// <param name="e">Revit Element</param>
        /// <returns>String IFCGUID</returns>
        public static string GetGUID(this Element e)
        {
            // generate IFC GUID using IFC API
            string ifcid = ExporterIFCUtils.CreateAlternateGUID(e);

            // fallback to uniqueId in case of error
            if (ifcid == null || ifcid == string.Empty)
            {
                return e.UniqueId;
            }

            return ifcid;
        }
    }
}