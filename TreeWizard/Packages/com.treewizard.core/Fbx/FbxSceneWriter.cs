using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TreeWizard.Fbx
{
    /// <summary>Writes an <see cref="FbxScene"/> as a binary FBX 7.4 file.</summary>
    public static class FbxSceneWriter
    {
        public static void WriteFile(string path, FbxScene scene, FbxWriteOptions options = null)
        {
            options = options ?? new FbxWriteOptions();
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            using (var file = File.Create(path))
                FbxBinaryWriter.Write(file, BuildDocument(scene, options, directory), options.CompressArrays);
        }

        /// <param name="baseDirectory">Folder the file will live in, used to resolve texture paths. Optional.</param>
        public static void Write(Stream output, FbxScene scene, FbxWriteOptions options = null, string baseDirectory = null)
        {
            options = options ?? new FbxWriteOptions();
            FbxBinaryWriter.Write(output, BuildDocument(scene, options, baseDirectory), options.CompressArrays);
        }

        /// <summary>The scene as FBX records, for inspection or custom writing.</summary>
        public static List<FbxNode> BuildDocument(FbxScene scene, FbxWriteOptions options, string baseDirectory = null) =>
            new DocumentBuilder(options ?? new FbxWriteOptions(), baseDirectory).Build(scene);

        sealed class DocumentBuilder
        {
            readonly FbxWriteOptions _options;
            readonly string _baseDirectory;
            readonly FbxNode _objects = new FbxNode("Objects");
            readonly FbxNode _connections = new FbxNode("Connections");
            readonly Dictionary<FbxMaterial, long> _materials = new Dictionary<FbxMaterial, long>();
            readonly Dictionary<string, long> _textures = new Dictionary<string, long>();
            long _nextId = 1_000_000;
            int _modelCount, _geometryCount;

            public DocumentBuilder(FbxWriteOptions options, string baseDirectory)
            {
                _options = options;
                _baseDirectory = baseDirectory;
            }

            public List<FbxNode> Build(FbxScene scene)
            {
                foreach (var model in scene.Models)
                    AddModel(model, 0);

                // Definitions need the object counts, so build them after the objects.
                return new List<FbxNode>
                {
                    HeaderExtension(scene.Creator),
                    new FbxNode("FileId", (byte[])FbxBinaryWriter.FileId.Clone()),
                    new FbxNode("CreationTime", FbxBinaryWriter.CreationTime),
                    new FbxNode("Creator", scene.Creator),
                    GlobalSettings(),
                    Documents(),
                    new FbxNode("References"),
                    Definitions(),
                    _objects,
                    _connections,
                    Takes(),
                };
            }

            long NextId() => _nextId++;

            void Connect(string type, long child, long parent, string property = null)
            {
                if (property == null)
                    _connections.Add("C", type, child, parent);
                else
                    _connections.Add("C", type, child, parent, property);
            }

            static void P(FbxNode properties70, string name, string type, string label, string flags, params object[] values)
            {
                var p = properties70.Add("P", name, type, label, flags);
                p.Properties.AddRange(values);
            }

            static string ObjectName(string name, string fbxClass) => $"{name}\0\u0001{fbxClass}";

            static FbxNode HeaderExtension(string creator)
            {
                var header = new FbxNode("FBXHeaderExtension");
                header.Add("FBXHeaderVersion", 1003);
                header.Add("FBXVersion", FbxBinaryWriter.Version);
                header.Add("EncryptionType", 0);
                var stamp = header.Add("CreationTimeStamp");
                stamp.Add("Version", 1000);
                stamp.Add("Year", 1970);
                stamp.Add("Month", 1);
                stamp.Add("Day", 1);
                stamp.Add("Hour", 10);
                stamp.Add("Minute", 0);
                stamp.Add("Second", 0);
                stamp.Add("Millisecond", 0);
                header.Add("Creator", creator);
                var info = header.Add("SceneInfo", ObjectName("GlobalInfo", "SceneInfo"), "UserData");
                info.Add("Type", "UserData");
                info.Add("Version", 100);
                var meta = info.Add("MetaData");
                meta.Add("Version", 100);
                foreach (var field in new[] { "Title", "Subject", "Author", "Keywords", "Revision", "Comment" })
                    meta.Add(field, "");
                return header;
            }

            FbxNode GlobalSettings()
            {
                var settings = new FbxNode("GlobalSettings");
                settings.Add("Version", 1000);
                var p = settings.Add("Properties70");
                // Y up, Z front, X right: the common interchange convention.
                P(p, "UpAxis", "int", "Integer", "", 1);
                P(p, "UpAxisSign", "int", "Integer", "", 1);
                P(p, "FrontAxis", "int", "Integer", "", 2);
                P(p, "FrontAxisSign", "int", "Integer", "", 1);
                P(p, "CoordAxis", "int", "Integer", "", 0);
                P(p, "CoordAxisSign", "int", "Integer", "", 1);
                P(p, "OriginalUpAxis", "int", "Integer", "", 1);
                P(p, "OriginalUpAxisSign", "int", "Integer", "", 1);
                P(p, "UnitScaleFactor", "double", "Number", "", _options.UnitScaleFactor);
                P(p, "OriginalUnitScaleFactor", "double", "Number", "", _options.UnitScaleFactor);
                P(p, "AmbientColor", "ColorRGB", "Color", "", 0d, 0d, 0d);
                P(p, "DefaultCamera", "KString", "", "", "Producer Perspective");
                P(p, "TimeMode", "enum", "", "", 11);
                P(p, "TimeSpanStart", "KTime", "Time", "", 0L);
                P(p, "TimeSpanStop", "KTime", "Time", "", 46186158000L);
                P(p, "CustomFrameRate", "double", "Number", "", 24d);
                return settings;
            }

            FbxNode Documents()
            {
                var documents = new FbxNode("Documents");
                documents.Add("Count", 1);
                var document = documents.Add("Document", NextId(), "", "Scene");
                var p = document.Add("Properties70");
                P(p, "SourceObject", "object", "", "");
                P(p, "ActiveAnimStackName", "KString", "", "", "");
                document.Add("RootNode", 0L);
                return documents;
            }

            FbxNode Definitions()
            {
                var counts = new List<(string type, int count)>
                {
                    ("GlobalSettings", 1),
                    ("Model", _modelCount),
                    ("Geometry", _geometryCount),
                    ("Material", _materials.Count),
                    ("Texture", _textures.Count),
                    ("Video", _textures.Count),
                };
                var definitions = new FbxNode("Definitions");
                definitions.Add("Version", 100);
                definitions.Add("Count", counts.Sum(c => c.count));
                foreach (var (type, count) in counts.Where(c => c.count > 0))
                    definitions.Add("ObjectType", type).Add("Count", count);
                return definitions;
            }

            static FbxNode Takes()
            {
                var takes = new FbxNode("Takes");
                takes.Add("Current", "");
                return takes;
            }

            void AddModel(FbxModel model, long parentId)
            {
                var id = NextId();
                _modelCount++;
                var flip = _options.ConvertFromLeftHanded;
                var t = model.Translation;
                var r = model.Rotation;
                var s = model.Scaling;

                var node = _objects.Add("Model", id, ObjectName(model.Name, "Model"), model.Mesh != null ? "Mesh" : "Null");
                node.Add("Version", 232);
                var p = node.Add("Properties70");
                // Mirroring X keeps rotations about X and flips the direction of those about Y and Z.
                P(p, "Lcl Translation", "Lcl Translation", "", "A", flip ? -(double)t.X : t.X, (double)t.Y, (double)t.Z);
                P(p, "Lcl Rotation", "Lcl Rotation", "", "A", (double)r.X, flip ? -(double)r.Y : r.Y, flip ? -(double)r.Z : r.Z);
                P(p, "Lcl Scaling", "Lcl Scaling", "", "A", (double)s.X, (double)s.Y, (double)s.Z);
                P(p, "DefaultAttributeIndex", "int", "Integer", "", 0);
                P(p, "InheritType", "enum", "", "", 1);
                node.Add("MultiLayer", 0);
                node.Add("MultiTake", 0);
                node.Add("Shading", true);
                node.Add("Culling", "CullingOff");
                Connect("OO", id, parentId);

                if (model.Mesh != null)
                    Connect("OO", AddGeometry(model.Mesh), id);
                foreach (var material in model.Materials)
                    Connect("OO", MaterialId(material), id); // connection order sets the material index
                foreach (var child in model.Children)
                    AddModel(child, id);
            }

            long AddGeometry(FbxMesh mesh)
            {
                var id = NextId();
                _geometryCount++;
                var flip = _options.ConvertFromLeftHanded;
                var geometry = _objects.Add("Geometry", id, ObjectName(mesh.Name ?? "Mesh", "Geometry"), "Mesh");
                geometry.Add("Properties70");
                geometry.Add("GeometryVersion", 124);

                var vertices = new double[mesh.Positions.Length * 3];
                for (var i = 0; i < mesh.Positions.Length; i++)
                {
                    var v = mesh.Positions[i];
                    vertices[i * 3] = flip ? -v.X : v.X;
                    vertices[i * 3 + 1] = v.Y;
                    vertices[i * 3 + 2] = v.Z;
                }
                geometry.Add("Vertices", vertices);

                // Corners in file order. Mirroring flips which way polygons face, so reverse them back.
                var corners = new int[mesh.Indices.Length];
                var polygonVertexIndex = new int[mesh.Indices.Length];
                var offset = 0;
                for (var poly = 0; poly < mesh.PolygonCount; poly++)
                {
                    var size = mesh.PolygonSizes?[poly] ?? 3;
                    for (var k = 0; k < size; k++)
                        corners[offset + k] = mesh.Indices[offset + (flip ? size - 1 - k : k)];
                    for (var k = 0; k < size; k++)
                        polygonVertexIndex[offset + k] = k == size - 1 ? ~corners[offset + k] : corners[offset + k]; // ~ marks a polygon's last corner
                    offset += size;
                }
                geometry.Add("PolygonVertexIndex", polygonVertexIndex);

                var layer = new FbxNode("Layer", 0);
                layer.Add("Version", 100);

                if (mesh.Normals != null)
                {
                    var normals = new double[corners.Length * 3];
                    for (var i = 0; i < corners.Length; i++)
                    {
                        var n = mesh.Normals[corners[i]];
                        normals[i * 3] = flip ? -n.X : n.X;
                        normals[i * 3 + 1] = n.Y;
                        normals[i * 3 + 2] = n.Z;
                    }
                    var element = geometry.Add("LayerElementNormal", 0);
                    element.Add("Version", 101);
                    element.Add("Name", "");
                    element.Add("MappingInformationType", "ByPolygonVertex");
                    element.Add("ReferenceInformationType", "Direct");
                    element.Add("Normals", normals);
                    AddLayerElement(layer, "LayerElementNormal");
                }

                if (mesh.Uvs != null)
                {
                    var uvs = new double[mesh.Uvs.Length * 2];
                    for (var i = 0; i < mesh.Uvs.Length; i++)
                    {
                        uvs[i * 2] = mesh.Uvs[i].X;
                        uvs[i * 2 + 1] = mesh.Uvs[i].Y;
                    }
                    var element = geometry.Add("LayerElementUV", 0);
                    element.Add("Version", 101);
                    element.Add("Name", "UVMap");
                    element.Add("MappingInformationType", "ByPolygonVertex");
                    element.Add("ReferenceInformationType", "IndexToDirect");
                    element.Add("UV", uvs);
                    element.Add("UVIndex", (int[])corners.Clone());
                    AddLayerElement(layer, "LayerElementUV");
                }

                {
                    var materials = mesh.PolygonMaterials;
                    var allSame = materials == null || materials.All(m => m == materials[0]);
                    var element = geometry.Add("LayerElementMaterial", 0);
                    element.Add("Version", 101);
                    element.Add("Name", "");
                    element.Add("MappingInformationType", allSame ? "AllSame" : "ByPolygon");
                    element.Add("ReferenceInformationType", "IndexToDirect");
                    element.Add("Materials", allSame ? new[] { materials?.FirstOrDefault() ?? 0 } : (int[])materials.Clone());
                    AddLayerElement(layer, "LayerElementMaterial");
                }

                geometry.Add(layer);
                return id;
            }

            static void AddLayerElement(FbxNode layer, string type)
            {
                var element = layer.Add("LayerElement");
                element.Add("Type", type);
                element.Add("TypedIndex", 0);
            }

            long MaterialId(FbxMaterial material)
            {
                if (_materials.TryGetValue(material, out var id))
                    return id;
                id = NextId();
                _materials[material] = id;

                var node = _objects.Add("Material", id, ObjectName(material.Name, "Material"), "");
                node.Add("Version", 102);
                node.Add("ShadingModel", "phong");
                node.Add("MultiLayer", 0);
                var p = node.Add("Properties70");
                var c = material.DiffuseColor;
                P(p, "DiffuseColor", "Color", "", "A", (double)c.X, (double)c.Y, (double)c.Z);
                P(p, "DiffuseFactor", "Number", "", "A", 1d);
                P(p, "SpecularFactor", "Number", "", "A", 0d);
                P(p, "Opacity", "double", "Number", "", 1d);

                if (!string.IsNullOrEmpty(material.DiffuseTexture))
                {
                    var texture = TextureId(material.DiffuseTexture);
                    Connect("OP", texture, id, "DiffuseColor");
                    if (material.AlphaFromDiffuseTexture)
                        Connect("OP", texture, id, "TransparentColor");
                }
                return id;
            }

            long TextureId(string path)
            {
                if (_textures.TryGetValue(path, out var id))
                    return id;

                string absolute, relative;
                if (Path.IsPathRooted(path))
                {
                    absolute = path;
                    relative = _baseDirectory != null ? Path.GetRelativePath(_baseDirectory, path) : Path.GetFileName(path);
                }
                else
                {
                    relative = path;
                    absolute = _baseDirectory != null ? Path.GetFullPath(Path.Combine(_baseDirectory, path)) : path;
                }
                absolute = absolute.Replace('\\', '/');
                relative = relative.Replace('\\', '/');
                var name = Path.GetFileNameWithoutExtension(path);

                var videoId = NextId();
                var video = _objects.Add("Video", videoId, ObjectName(name, "Video"), "Clip");
                video.Add("Type", "Clip");
                var vp = video.Add("Properties70");
                P(vp, "Path", "KString", "XRefUrl", "", absolute);
                P(vp, "RelPath", "KString", "XRefUrl", "", relative);
                video.Add("UseMipMap", 0);
                video.Add("Filename", absolute);
                video.Add("RelativeFilename", relative);

                id = NextId();
                _textures[path] = id;
                var texture = _objects.Add("Texture", id, ObjectName(name, "Texture"), "");
                texture.Add("Type", "TextureVideoClip");
                texture.Add("Version", 202);
                texture.Add("TextureName", ObjectName(name, "Texture"));
                var tp = texture.Add("Properties70");
                P(tp, "UseMaterial", "bool", "", "", 1);
                P(tp, "UseMipMap", "bool", "", "", 0);
                texture.Add("Media", ObjectName(name, "Video"));
                texture.Add("FileName", absolute);
                texture.Add("RelativeFilename", relative);
                texture.Add("ModelUVTranslation", 0d, 0d);
                texture.Add("ModelUVScaling", 1d, 1d);
                texture.Add("Texture_Alpha_Source", "None");
                texture.Add("Cropping", 0, 0, 0, 0);

                Connect("OO", videoId, id);
                return id;
            }
        }
    }
}
