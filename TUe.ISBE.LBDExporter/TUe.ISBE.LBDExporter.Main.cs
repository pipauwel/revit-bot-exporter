using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Mechanical;
using System.Windows.Forms;
using Autodesk.Revit.DB.Architecture;

namespace TUe.ISBE.LBDExporter
{
    [Transaction(TransactionMode.Manual)]
    public class Main : IExternalCommand
    {
        private Document doc;
        //Dictionary with element IDs and namespaces
        private Dictionary<ElementId, string> ElementDict = new Dictionary<ElementId, string>();
        private Dictionary<ElementId, String> virtualelements = new Dictionary<ElementId, string>();
        
        private static String Namespace = "https://linkedbuildingdata.net/ifc/resources" + DateTime.Now.ToString("yyyyMMdd_hhmmss") + "/";

        private readonly string NL = Environment.NewLine;
        private readonly string NLT = Environment.NewLine + "\t";

        private List<Element> spaces;
        private IList<Element> WinDoor;

        private void Export(
          ExternalCommandData commandData)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            doc = uidoc.Document;
            Transaction tx = new Transaction(doc);            

            SaveFileDialog savefile = new SaveFileDialog();
            // set a default file name
            savefile.FileName = doc.Title.Split(".rvt".ToCharArray())[0] + ".ttl";
            // set filters - this can be done in properties as well
            savefile.Filter = "Text files (*.ttl)|*.ttl|All files (*.*)|*.*";

            if (savefile.ShowDialog() == DialogResult.OK)
            {
                String prefixes =
                         "@prefix bot:\t<https://w3id.org/bot#> ." +
                    NL + "@prefix rdfs:\t<http://www.w3.org/2000/01/rdf-schema#> ." +
                    NL + "@prefix rdf:\t<http://www.w3.org/1999/02/22-rdf-syntax-ns#> ." +
                    NL + "@prefix xsd:\t<http://www.w3.org/2001/XMLSchema#> ." +
                    NL + "@prefix beo:\t<https://pi.pauwel.be/voc/buildingelement/> ." +
                    NL + "@prefix mep:\t<https://pi.pauwel.be/voc/distributionelement/> ." +
                    NL + "@prefix props:\t<https://w3id.org/props#> ." +
                    NL + "@prefix fog:\t<https://w3id.org/fog#> ." +
                    NL + $"@prefix inst:\t<{Namespace}> ." + NL;

                String roomsString = GetRooms();
                String wallsString = GetWalls();
                //String wallTypesString = GetWallTypes();
                String windowsDoorsString = GetWindowsDoors();
                String storeyString = GetStoreys();
                String relationsString = GetRelations();
                
                using (StreamWriter writer =
                new StreamWriter(savefile.FileName))
                {
                    writer.Write(prefixes);
                    writer.Write(roomsString);
                    writer.Write(wallsString);
                    //writer.Write(wallTypesString);
                    writer.Write(windowsDoorsString);
                    writer.Write(storeyString);
                    writer.Write(relationsString);
                }
            }
        }


        private String GetRooms()
        {
            String tString = "";

            spaces = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement)).WhereElementIsNotElementType()
                  .Where(X => X.Category.Name == "Spaces" || X.Category.Name == "Rooms").ToList<Element>();

            int interfaceCounter = 1;
            int virtualElementCounter = 1;            

            foreach (Element e in spaces)
            {
                //string URI = Parameters.GenerateURIifNotExist(doc, e).Replace(Namespace, "inst:");
                string URI = Util.CreateURI(e,Namespace).Replace(Namespace, "inst:");
                ElementDict.Add(e.Id, URI);
                //URItranslater.Add(URI,"inst:")

                if (e.Category.Name == "Spaces")
                {
                    Space space = e as Space;

                    tString +=
                        NL + NL + $"{URI}" +
                        NLT + "a bot:Space ";

                    tString += ";" +
                        NLT + $"props:id \"{space.Id}\" ;" +
                        NLT + $"props:guid \"{space.GetIFCGUID()}\" ;" +
                        NLT + $"props:number \"{space.Number}\"^^xsd:string .";

                    IList<IList<Autodesk.Revit.DB.BoundarySegment>> segments = space.GetBoundarySegments(new SpatialElementBoundaryOptions());
                    if (null != segments)  //the room may not be bound
                    {
                        foreach (IList<Autodesk.Revit.DB.BoundarySegment> segmentList in segments)
                        {
                            foreach (Autodesk.Revit.DB.BoundarySegment boundarySegment in segmentList)
                            {
                                Element boundingEl = doc.GetElement(boundarySegment.ElementId);
                                if (boundingEl == null)
                                {
                                    Console.Out.WriteLine("Found null buildingEl: " + boundarySegment.ToString());
                                    continue;
                                }

                                //-2000066
                                String eln = boundingEl.Name;
                                if (boundingEl.Category.Name == "<Room Separation>") // == BuiltInCategory.OST_RoomSeparationLines          
                                {
                                    String virtualElement = "inst:VirtualElement_" + virtualElementCounter;
                                    if (virtualelements.ContainsKey(boundingEl.Id))
                                    {
                                        virtualElement = virtualelements[boundingEl.Id];
                                    }
                                    else
                                    {
                                        tString +=
                                            NL + "inst:VirtualElement_" + virtualElementCounter +
                                            NLT + "a bot:VirtualElement ." + NL;

                                        virtualelements.Add(boundingEl.Id, "inst:VirtualElement_" + virtualElementCounter);
                                    }

                                    tString +=
                                        NL + "inst:Interface_" + interfaceCounter +
                                        NLT + "a bot:Interface ;" +
                                        NLT + "bot:interfaceOf " + URI + ", " + virtualElement + " ;" +
                                        NLT + "fog:asSfa_v2-wkt \"LINESTRING (" + (boundarySegment.GetCurve().GetEndPoint(0).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(0).Y * 12 * 25.4).ToString().Replace(',', '.') + ", "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).Y * 12 * 25.4).ToString().Replace(',', '.') + ")\" ." + NL;

                                    interfaceCounter++;
                                    virtualElementCounter++;
                                }
                                else
                                {
                                    tString +=
                                        NL + "inst:Interface_" + interfaceCounter +
                                        NLT + "a bot:Interface ;" +
                                        NLT + "bot:interfaceOf " + URI + ", inst:" + boundingEl.Category.Name + "_" + boundingEl.GetIFCGUID() + " ;" +
                                        NLT + "fog:asSfa_v2-wkt \"LINESTRING ("
                                                    + (boundarySegment.GetCurve().GetEndPoint(0).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(0).Y * 12 * 25.4).ToString().Replace(',', '.') + ", "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).Y * 12 * 25.4).ToString().Replace(',', '.') + ")\" ." + NL;

                                    interfaceCounter++;
                                }
                            }
                        }
                    }
                }

                if (e.Category.Name == "Rooms")
                {
                    Room room = e as Room;

                    tString +=
                        NL + NL + $"{URI}" +
                        NLT + "a bot:Space ";

                    tString += ";" +
                        NLT + $"props:id \"{room.Id}\" ;" +
                        NLT + $"props:guid \"{room.GetIFCGUID()}\" ;" +
                        NLT + $"props:number \"{room.Number}\"^^xsd:string ." + NL;

                    IList<IList<Autodesk.Revit.DB.BoundarySegment>> segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                    if (null != segments)  //the room may not be bound
                    {
                        foreach (IList<Autodesk.Revit.DB.BoundarySegment> segmentList in segments)
                        {
                            foreach (Autodesk.Revit.DB.BoundarySegment boundarySegment in segmentList)
                            {
                                Element boundingEl = doc.GetElement(boundarySegment.ElementId);
                                if (boundingEl == null)
                                    continue;

                                if (boundingEl.Category.Name == "Walls")
                                {
                                    Wall x = (Wall)boundingEl;
                                    IList<ElementId> inserts = x.FindInserts(true, false, false, false);
                                    foreach (ElementId id in inserts)
                                    {
                                        Element eli = doc.GetElement(id);
                                        if (eli.Category.Name == "Doors")
                                        {
                                            FamilyInstance d = (FamilyInstance)eli;
                                            GeometryElement ge = d.get_Geometry(new Options());

                                            // Get geometry object
                                            foreach (GeometryObject geoObject in ge)
                                            {
                                                // Get the geometry instance which contains the geometry information
                                                Autodesk.Revit.DB.GeometryInstance instance =
                                                       geoObject as Autodesk.Revit.DB.GeometryInstance;
                                                if (null != instance)
                                                {
                                                    GeometryElement instanceGeometryElement = instance.GetInstanceGeometry();
                                                    foreach (GeometryObject o in instanceGeometryElement)
                                                    {
                                                        // Try to find curves
                                                        Curve curve = o as Curve;
                                                        if (curve != null)
                                                        {
                                                            // The curve is already transformed into the project coordinate system
                                                            tString +=
                                                                NL + "inst:Interface_" + interfaceCounter +
                                                                NLT + "a bot:Interface ;" +
                                                                NLT + "bot:interfaceOf " + URI + ", inst:" + eli.Category.Name + "_" + eli.GetIFCGUID() + " ;" +
                                                                NLT + "fog:asSfa_v2-wkt \"LINESTRING ("
                                                                    + (curve.GetEndPoint(0).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                                    + (curve.GetEndPoint(0).Y * 12 * 25.4).ToString().Replace(',', '.') + ", "
                                                                    + (curve.GetEndPoint(1).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                                    + (curve.GetEndPoint(1).Y * 12 * 25.4).ToString().Replace(',', '.') + ")\" ." + NL;

                                                            interfaceCounter++;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }

                                        }
                                    }
                                }

                                String eln = boundingEl.Name;
                                if (boundingEl.Category.Name == "<Room Separation>") // == BuiltInCategory.OST_RoomSeparationLines                                    
                                {
                                    String virtualElement = "inst:VirtualElement_" + virtualElementCounter;
                                    if (virtualelements.ContainsKey(boundingEl.Id))
                                    {
                                        virtualElement = virtualelements[boundingEl.Id];
                                    }
                                    else
                                    {
                                        tString +=
                                            NL + "inst:VirtualElement_" + virtualElementCounter +
                                            NLT + "a bot:VirtualElement ." + NL;

                                        virtualelements.Add(boundingEl.Id, "inst:VirtualElement_" + virtualElementCounter);
                                    }

                                    tString +=
                                        NL + "inst:Interface_" + interfaceCounter +
                                        NLT + "a bot:Interface ;" +
                                        NLT + "bot:interfaceOf " + URI + ", " + virtualElement + " ;" +
                                        NLT + "fog:asSfa_v2-wkt \"LINESTRING (" + (boundarySegment.GetCurve().GetEndPoint(0).X).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(0).Y * 12 * 25.4).ToString().Replace(',', '.') + ", "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).Y * 12 * 25.4).ToString().Replace(',', '.') + ")\" ." + NL;

                                    interfaceCounter++;
                                    virtualElementCounter++;
                                }
                                else
                                {
                                    tString +=
                                        NL + "inst:Interface_" + interfaceCounter +
                                        NLT + "a bot:Interface ;" +
                                        NLT + "bot:interfaceOf " + URI + ", inst:" + boundingEl.Category.Name + "_" + boundingEl.GetIFCGUID() + " ;" +
                                        NLT + "fog:asSfa_v2-wkt \"LINESTRING ("
                                                    + (boundarySegment.GetCurve().GetEndPoint(0).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(0).Y * 12 * 25.4).ToString().Replace(',', '.') + ", "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).X * 12 * 25.4).ToString().Replace(',', '.') + " "
                                                    + (boundarySegment.GetCurve().GetEndPoint(1).Y * 12 * 25.4).ToString().Replace(',', '.') + ")\" ." + NL;

                                    interfaceCounter++;
                                }
                            }
                        }
                    }

                }

            }

            return tString;
        }

        private String GetWalls()
        {
            String tString = "";

            List<Element> walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall)).WhereElementIsNotElementType().ToElements().ToList();

            tString += NL + NL + "# WALLS";

            foreach (Element e in walls)
            {

                Wall wall = e as Wall;

                //string URI = Parameters.GenerateURIifNotExist(doc, e).Replace(Namespace, "inst:");
                string URI = Util.CreateURI(e, Namespace).Replace(Namespace, "inst:");

                String wallType = doc.GetElement(wall.GetTypeId()).Name.ToString();
                //string typeURI = "inst:" + Util.TypeNameToId(wallType);

                ElementDict.Add(e.Id, URI);

                // Append classes to 
                tString +=
                    NL + NL + $"{URI}" +
                    NLT + $"a bot:Element ;" +
                    NLT + $"a beo:Wall ;" +
                    NLT + $"props:id \"{e.Id}\" ;" +
                    NLT + $"props:guid \"{e.GetIFCGUID()}\" ;";

                string width = Math.Round(UnitUtils.ConvertFromInternalUnits(wall.Width, Autodesk.Revit.DB.DisplayUnitType.DUT_MILLIMETERS), 2).ToString().Replace(",", ".");
                double curveLength = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble();
                string length = Math.Round(UnitUtils.ConvertFromInternalUnits(curveLength, Autodesk.Revit.DB.DisplayUnitType.DUT_MILLIMETERS), 2).ToString().Replace(",", ".");

                width = $"\"{width}\"^^xsd:decimal";
                length = $"\"{length}\"^^xsd:decimal";
                string name = $"{e.Name}";

                tString += NLT + $"props:identityDataName \"{name}\" ;";
                tString += NLT + $"props:dimensionsWidth {width} ;";
                tString += NLT + $"props:dimensionsLength {length} ;";                

                //testString += Util.GetFacesAndEdges(wall) + NL;
                tString += NLT + $"bot:hasSimple3DModel \"{Util.GetFacesAndEdges(wall)}\" .";               

            }

            return tString;
        }

        // These are all inexisting classes - seems weird to define these here.
        /*private String GetWallTypes()
        {
            String tString = ""; 

            List<Element> wallTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType)).ToElements().ToList();

            tString += NL + NL + "# WALLTYPES";

            foreach (Element e in wallTypes)
            {
                WallType wt = e as WallType;

                string URI = "inst:" + Util.TypeNameToId(wt.Name);

                tString +=
                    NL + URI +
                    NLT + "rdfs:subClassOf bot:Element ;" +
                    NLT + $"rdfs:label \"{wt.Name}\" .";
            }

            return tString;
        }*/

        private String GetWindowsDoors()
        {
            String tString = "";

            WinDoor = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType().WherePasses(new LogicalOrFilter(new List<ElementFilter>
                        {
                            new ElementCategoryFilter(BuiltInCategory.OST_Windows),
                            new ElementCategoryFilter(BuiltInCategory.OST_Doors)

                         })).ToElements();

            tString += NL + NL + "# WINDOWS & DOORS";

            foreach (Element e in WinDoor)
            {
                //string URI = Parameters.GenerateURIifNotExist(doc, e).Replace(Namespace, "inst:");
                string URI = Util.CreateURI(e, Namespace).Replace(Namespace, "inst:");
                ElementDict.Add(e.Id, URI);
                string name = $"{e.Name}";
                Console.Out.WriteLine(e.GetType().Name);

                tString +=
                    NL + $"{URI}" +
                    NLT + "a bot:Element ;" +
                    NLT + "a beo:"+ e.GetType().Name + " ;" +
                    NLT + $"props:id \"{e.Id}\" ;" +
                    NLT + "props:guid \"" + e.GetIFCGUID() + "\" ;";

                tString += NLT + $"props:identityDataName \"{name}\" ." + NL;
            }

            return tString;
        }

        private String GetStoreys()
        {
            String tString = "";

            List<Level> levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).WhereElementIsNotElementType().ToElements().Cast<Level>()
                    .ToList();

            tString += NL + NL + "### STOREYS ###";

            foreach (Level e in levels)
            {
                //string URI = Parameters.GenerateURIifNotExist(doc, e).Replace(Namespace, "inst:");
                string URI = Util.CreateURI(e, Namespace).Replace(Namespace, "inst:");
                
                ElementDict.Add(e.Id, URI);

                tString +=
                    NL + URI +
                    NLT + "a bot:Storey ;" +
                    NLT + $"props:id \"{e.Id}\" ;" +
                    NLT + "props:guid \"" + e.GetIFCGUID() + "\" .";

                string name = $"\"{e.Name}\"";

                tString += NLT + $"props:identityDataName {name} ." + NL;
            }

            return tString;
        }

        private String GetRelations()
        {
            String tString = NL + NL + "### RELATIONSHIPS ###";
            
            foreach (Element e in WinDoor)
            {
                try
                {
                    FamilyInstance FamIns = e as FamilyInstance;

                    tString +=
                            NL + $"{ElementDict[FamIns.Host.Id]} bot:hasSubElement {ElementDict[e.Id]} .";
                }
                catch { }
            }

            tString += NL + NL + "# ROOMS AT EACH STOREY";

            foreach (Element e in spaces)
            {

                try
                {
                    tString +=
                    NL + $"{ElementDict[e.LevelId]} bot:hasSpace {ElementDict[e.Id]} .";
                }
                catch { }
            }

            tString += NL + NL + "# ELEMENTS ADJACENT TO ROOMS";

            foreach (SpatialElement sp in spaces)
            {
                SpatialElementBoundaryOptions SpaEleBdOp = new SpatialElementBoundaryOptions();
                IList<IList<BoundarySegment>> BdSegLoops = sp.GetBoundarySegments(SpaEleBdOp);

                foreach (IList<BoundarySegment> BdSegLoop in BdSegLoops)
                    foreach (BoundarySegment BdSeg in BdSegLoop)
                    {
                        ElementId id = null;

                        try
                        {
                            id = BdSeg.ElementId;

                            if (doc.GetElement(id).Category.Name == "Walls")
                            {

                                tString +=
                                    NL + $"{ElementDict[sp.Id]} bot:adjacentElement {ElementDict[id]} .";
                            }
                        }
                        catch
                        { }
                    }
            }

            return tString;
        }
        
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            Export(commandData);
            return Result.Succeeded;
        }


        public static String GetNamespace()
        {
            return Namespace;
        }
    }
}
