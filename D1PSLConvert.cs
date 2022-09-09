/*
    Copyright (c) 2022 The LibDescent Team, Arne de Bruijn

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/
using System;
using LibDescent.Data;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace D1PSLConvert
{
    class PSXSegment
    {
        public ushort[] Normals { get; set; }
        public ushort[] Sides { get; set; }
        public short[] Children { get; set; }
        public ushort[] Vertices { get; set; }
        public short Objects { get; set; }
        public byte Special { get; set; }
        public sbyte Matcen { get; set; }
        public Fix Light { get; set; }
        public short Value { get; set; }
    }
    
    class PSXSide
    {
        public short Wall { get; set; }
        public ushort TMap { get; set; }
        public ushort TMap2 { get; set; }
        public short[] U { get; set; }
        public short[] V { get; set; }
        public byte[] L { get; set; }
    }
    
    public class D1PSLReader
    {
        private class Reader
        {
            private const int MaxReactorTriggerTargets = 10;
            public D1Level Level = new D1Level();
            private Dictionary<Wall, byte> _wallTriggerLinks = new Dictionary<Wall, byte>();
            private Dictionary<Wall, uint> _wallLinked = new Dictionary<Wall, uint>();
        
            protected static FixVector ReadFixVector(BinaryReader reader)
            {
                return FixVector.FromRawValues(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            }

            protected static FixAngles ReadFixAngles(BinaryReader reader)
            {
                return FixAngles.FromRawValues(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
            }

            protected static FixMatrix ReadFixMatrix(BinaryReader reader)
            {
                return new FixMatrix(ReadFixVector(reader), ReadFixVector(reader), ReadFixVector(reader));
            }

            private LevelObject ReadObject(BinaryReader reader)
            {
                int readSize;

                var levelObject = new LevelObject();
                reader.ReadInt32(); // signature
                levelObject.Type = (ObjectType)reader.ReadSByte();
                levelObject.SubtypeID = reader.ReadByte();
                _ = reader.ReadInt32(); // next, prev
                levelObject.ControlType = ControlTypeFactory.NewControlType((ControlTypeID)reader.ReadByte());
                levelObject.MoveType = MovementTypeFactory.NewMovementType((MovementTypeID)reader.ReadByte());
                levelObject.RenderType = RenderTypeFactory.NewRenderType((RenderTypeID)reader.ReadByte());
                levelObject.Flags = reader.ReadByte();
                levelObject.MultiplayerOnly = false;
                levelObject.Segnum = reader.ReadInt16();
                levelObject.AttachedObject = reader.ReadInt16();
                _ = reader.ReadInt16(); // pad
                levelObject.Position = ReadFixVector(reader);
                levelObject.Orientation = ReadFixMatrix(reader);
                levelObject.Size = new Fix(reader.ReadInt32());
                levelObject.Shields = new Fix(reader.ReadInt32());
                levelObject.LastPos = ReadFixVector(reader);
                levelObject.ContainsType = (ObjectType)reader.ReadByte();
                levelObject.ContainsId = reader.ReadByte();
                levelObject.ContainsCount = reader.ReadByte();
                reader.ReadByte(); // matcen creator
                reader.ReadInt32(); // lifeleft

                readSize = 0;
                switch (levelObject.MoveType)
                {
                    case PhysicsMoveType physics:
                        physics.Velocity = ReadFixVector(reader);
                        physics.Thrust = ReadFixVector(reader);
                        physics.Mass = new Fix(reader.ReadInt32());
                        physics.Drag = new Fix(reader.ReadInt32());
                        physics.Brakes = new Fix(reader.ReadInt32());
                        physics.AngularVel = ReadFixVector(reader);
                        physics.RotationalThrust = ReadFixVector(reader);
                        physics.Turnroll = reader.ReadInt16();
                        physics.Flags = (PhysicsFlags)reader.ReadInt16();
                        readSize = 64;
                        break;
                    case SpinningMoveType spin:
                        spin.SpinRate = ReadFixVector(reader);
                        readSize = 4;
                        break;
                    default:
                        break;
                }
                _ = reader.ReadBytes(64 - readSize);

                readSize = 0;
                switch (levelObject.ControlType)
                {
                    case AIControl ai:
                        ai.Behavior = reader.ReadByte();
                        for (int i = 0; i < AIControl.NumAIFlags; i++)
                            ai.AIFlags[i] = reader.ReadByte();

                        ai.HideSegment = reader.ReadInt16();
                        ai.HideIndex = reader.ReadInt16();
                        ai.PathLength = reader.ReadInt16();
                        ai.CurPathIndex = reader.ReadInt16();

                        readSize = 20;
                        break;
                    case ExplosionControl explosion:
                        explosion.SpawnTime = new Fix(reader.ReadInt32());
                        explosion.DeleteTime = new Fix(reader.ReadInt32());
                        explosion.DeleteObject = reader.ReadInt16();
                        readSize = 10;
                        break;
                    case PowerupControl powerup:
                        powerup.Count = reader.ReadInt32();
                        readSize = 4;
                        break;
                    case WeaponControl weapon:
                        weapon.ParentType = reader.ReadInt16();
                        weapon.ParentNum = reader.ReadInt16();
                        weapon.ParentSig = reader.ReadInt32();
                        readSize = 8;
                        break;
                    case LightControl light:
                        light.Intensity = new Fix(reader.ReadInt32());
                        readSize = 4;
                        break;
                }
                _ = reader.ReadBytes(32 - readSize);

                readSize = 0;
                switch (levelObject.RenderType)
                {
                    case PolymodelRenderType pm:
                        {
                            pm.ModelNum = reader.ReadInt32();
                            for (int i = 0; i < Polymodel.MaxSubmodels; i++)
                            {
                                pm.BodyAngles[i] = ReadFixAngles(reader);
                            }
                            pm.Flags = reader.ReadInt32();
                            pm.TextureOverride = reader.ReadInt32();
                            readSize = 72;
                        }
                        break;
                    case FireballRenderType fb:
                        fb.VClipNum = reader.ReadInt32();
                        fb.FrameTime = new Fix(reader.ReadInt32());
                        fb.FrameNumber = reader.ReadByte();
                        readSize = 9;
                        break;
                }
                _ = reader.ReadBytes(76 - readSize);

                return levelObject;
            }

            MatCenter ReadMatcen(BinaryReader reader)
            {
                uint robotFlags = reader.ReadUInt32();
                var hitPoints = reader.ReadInt32();
                var interval = reader.ReadInt32();
                var segmentNum = reader.ReadInt16();
                _ = reader.ReadInt16(); // fuelcen number - not needed

                MatCenter matcen = new MatCenter(Level.Segments[segmentNum]);
                for (int i = 0; i < 32; i++)
                    if ((robotFlags & (1 << i)) != 0)
                        matcen.SpawnedRobotIds.Add((uint)i);
                matcen.HitPoints = new Fix(hitPoints);
                matcen.Interval = new Fix(interval);
                return matcen;
            }

            ushort[] ReadUInt16Array(BinaryReader reader, int count)
            {
                var values = new ushort[count];
                for (int i = 0; i < count; i++)
                    values[i] = reader.ReadUInt16();
                return values;
            }

            short[] ReadInt16Array(BinaryReader reader, int count)
            {
                var values = new short[count];
                for (int i = 0; i < count; i++)
                    values[i] = reader.ReadInt16();
                return values;
            }

            PSXSegment ReadPSXSegment(BinaryReader reader)
            {
                var segment = new PSXSegment();
                segment.Normals = ReadUInt16Array(reader, 6);
                segment.Sides = ReadUInt16Array(reader, 6);
                segment.Children = ReadInt16Array(reader, 6);
                segment.Vertices = ReadUInt16Array(reader, 8);
                segment.Objects = reader.ReadInt16();
                segment.Special = reader.ReadByte();
                segment.Matcen = reader.ReadSByte();
                segment.Light = new Fix(reader.ReadInt32());
                segment.Value = reader.ReadInt16();
                reader.ReadInt16(); // pad
                return segment;
            }

            PSXSide ReadPSXSide(BinaryReader reader)
            {
                var side = new PSXSide();
                side.Wall = reader.ReadInt16();
                side.TMap = reader.ReadUInt16();
                side.TMap2 = reader.ReadUInt16();
                side.U = ReadInt16Array(reader, 4);
                side.V = ReadInt16Array(reader, 4);
                side.L = reader.ReadBytes(4);
                return side;
            }

            protected static (short segmentNum, short sideNum)[] ReadFixedLengthTargetList(BinaryReader reader, int targetListLength)
            {
                var targetList = new (short segmentNum, short sideNum)[targetListLength];
                for (int i = 0; i < targetListLength; i++)
                {
                    targetList[i].segmentNum = reader.ReadInt16();
                }
                for (int i = 0; i < targetListLength; i++)
                {
                    targetList[i].sideNum = reader.ReadInt16();
                }
                return targetList;
            }

            protected D1Trigger ReadTrigger(BinaryReader reader)
            {
                var trigger = new D1Trigger();
                trigger.Type = (TriggerType)reader.ReadByte();
                _ = reader.ReadByte(); // pad
                trigger.Flags = (D1TriggerFlags)reader.ReadUInt16();
                trigger.Value = new Fix(reader.ReadInt32());
                trigger.Time = reader.ReadInt32();
                _ = reader.ReadByte(); // link_num - does nothing
                _ = reader.ReadByte(); // pad
                var numLinks = reader.ReadInt16();

                var targets = ReadFixedLengthTargetList(reader, D1Trigger.MaxWallsPerLink);
                for (int i = 0; i < numLinks; i++)
                {
                    var side = Level.Segments[targets[i].segmentNum].Sides[targets[i].sideNum];
                    trigger.Targets.Add(side);
                }

                return trigger;
            }

            Wall ReadWall(BinaryReader reader)
            {
                var segmentNum = reader.ReadInt32();
                var sideNum = reader.ReadInt32();
                var side = Level.Segments[segmentNum].Sides[sideNum];

                Wall wall = new Wall(side);
                wall.HitPoints = new Fix(reader.ReadInt32());
                var linkedWall = reader.ReadInt32();
                if (linkedWall != -1)
                    _wallLinked[wall] = (uint)linkedWall;
                wall.Type = (WallType)reader.ReadByte();
                wall.Flags = (WallFlags)reader.ReadByte();
                wall.State = (WallState)reader.ReadByte();
                var triggerNum = reader.ReadByte();
                if (triggerNum != 0xFF)
                {
                    _wallTriggerLinks[wall] = triggerNum;
                }
                wall.DoorClipNumber = reader.ReadByte();
                wall.Keys = (WallKeyFlags)reader.ReadByte();
                _ = reader.ReadByte(); // controlling trigger - will recalculate
                wall.CloakOpacity = reader.ReadByte();
                return wall;
            }

            public void Read(BinaryReader reader)
            {
                List<PSXSegment> psxSegments = new List<PSXSegment>();
                List<PSXSide> psxSides = new List<PSXSide>();

                Level.LevelName = new string(reader.ReadBytes(36)
                    .TakeWhile(x => x != 0).Select(x => (char)x).ToArray());
                int numObjects = reader.ReadInt32() + 1;
                int numWalls = reader.ReadInt32();
                int numDoors = reader.ReadInt32();
                int numTriggers = reader.ReadInt32();
                int numMatcens = reader.ReadInt32();
                int numVertices = reader.ReadInt32();
                int numSegments = reader.ReadInt32();
                int numSides = reader.ReadInt32();
                int numNormals = reader.ReadInt32();

                // Allocate segments/sides before reading data so we don't need a separate linking phase for them
                for (int i = 0; i < numSegments; i++)
                {
                    var segment = new Segment();
                    for (uint sideNum = 0; sideNum < Segment.MaxSides; sideNum++)
                    {
                        segment.Sides[sideNum] = new Side(segment, sideNum);
                    }
                    Level.Segments.Add(segment);
                }

                // Objects
                for (int i = 0; i < numObjects; i++)
                {
                    Level.Objects.Add(ReadObject(reader));
                }

                // Walls
                for (int i = 0; i < numWalls; i++)
                {
                    Level.Walls.Add(ReadWall(reader));
                }

                foreach (var wallLink in _wallLinked)
                {
                    wallLink.Key.LinkedWall = Level.Walls[(int)wallLink.Value];
                }

                // Triggers
                for (int i = 0; i < numTriggers; i++)
                {
                    var trigger = ReadTrigger(reader);
                    (Level as D1Level).Triggers.Add(trigger);
                    for (int targetNum = 0; targetNum < trigger.Targets.Count; targetNum++)
                    {
                        trigger.Targets[targetNum].Wall?.ControllingTriggers.Add((trigger, (uint)targetNum));
                    }
                }

                foreach (var wallTriggerLink in _wallTriggerLinks)
                {
                    wallTriggerLink.Key.Trigger = Level.Triggers[wallTriggerLink.Value];
                    Level.Triggers[wallTriggerLink.Value].ConnectedWalls.Add(wallTriggerLink.Key);
                }

                var numReactorTriggerTargets = reader.ReadInt16();

                var targets = ReadFixedLengthTargetList(reader, MaxReactorTriggerTargets);

                for (int targetNum = 0; targetNum < numReactorTriggerTargets; targetNum++)
                {
                    var side = Level.Segments[targets[targetNum].segmentNum].Sides[targets[targetNum].sideNum];
                    Level.ReactorTriggerTargets.Add(side);
                }

                // Matcens
                for (int i = 0; i < numMatcens; i++)
                {
                    (Level as D1Level).MatCenters.Add(ReadMatcen(reader));
                }

                // Vertices
                for (int i = 0; i < numVertices; i++)
                {
                    var vector = ReadFixVector(reader);
                    var vertex = new LevelVertex(vector);
                    Level.Vertices.Add(vertex);
                }

                // Segments
                for (int i = 0; i < numSegments; i++)
                {
                    psxSegments.Add(ReadPSXSegment(reader));
                }

                // Sides
                for (int i = 0; i < numSides; i++)
                {
                    psxSides.Add(ReadPSXSide(reader));
                }

                // Normals ignored, they are recalculated

                for (int i = 0; i < numSegments; i++)
                {
                    PSXSegment psxSegment = psxSegments[i];
                    Segment segment = Level.Segments[i];
                    for (int j = 0; j < Segment.MaxSides; j++)
                    {
                        PSXSide psxSide = psxSides[psxSegment.Sides[j]];
                        Side side = segment.Sides[j];
                        if (psxSide.Wall != -1)
                            side.Wall = Level.Walls[psxSide.Wall];
                        if (psxSegment.Children[j] == -2)
                             side.Exit = true;
                        else if (psxSegment.Children[j] >= 0)
                            side.ConnectedSegment = Level.Segments[psxSegment.Children[j]];
                        side.BaseTextureIndex = psxSide.TMap;
                        side.OverlayTextureIndex = (ushort)(psxSide.TMap2 & 0x3fff);
                        side.OverlayRotation = (OverlayRotation)(psxSide.TMap2 >> 14);
                        for (int k = 0; k < Side.MaxVertices; k++)
                        {
                            side.Uvls[k].U = new Fix((psxSide.U[k] - 0x40) << 10);
                            side.Uvls[k].V = new Fix((psxSide.V[k] - 0x40) << 10);
                            side.Uvls[k].L = new Fix(psxSide.L[k] << 9);
                        }
                    }
                    for (int vertexNum = 0; vertexNum < Segment.MaxVertices; vertexNum++)
                    {
                        segment.Vertices[vertexNum] = Level.Vertices[psxSegment.Vertices[vertexNum]];
                        segment.Vertices[vertexNum].ConnectedSegments.Add((segment, (uint)vertexNum));
                    }
                    // Connect vertices to sides
                    foreach (var side in segment.Sides)
                    {
                        for (int vertexNum = 0; vertexNum < side.GetNumVertices(); vertexNum++)
                        {
                            side.GetVertex(vertexNum).ConnectedSides.Add((side, (uint)vertexNum));
                        }
                    }
                    segment.Function = (SegFunction)psxSegment.Special;
                    if (psxSegment.Matcen != -1 && psxSegment.Matcen < numMatcens)
                        segment.MatCenter = Level.MatCenters[psxSegment.Matcen];
                    segment.Light = psxSegment.Light;
                }
            }
        }
        
        public static D1Level CreateFromStream(Stream stream)
        {
            Reader r = new Reader();
            r.Read(new BinaryReader(stream));
            return r.Level;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Convert D1 Playstation levels to regular D1 levels");
                Console.WriteLine("Usage: d1pslconvert <input.psl> [<output.rdl>]");
                return;
            }

            string inputPath = args[0];
            string outputPath = args.Length >= 2 ? args[1] : Path.ChangeExtension(inputPath, ".rdl");
            ILevel level;

            using (var stream = File.OpenRead(inputPath))
            {
                level = D1PSLReader.CreateFromStream(stream);
            }

            using (var stream = File.Create(outputPath))
            {
                level.WriteToStream(stream);
            }

            Console.WriteLine("Wrote " + outputPath);
        }
    }
}
