﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ImGuiNET;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Replanetizer.Tools;
using static LibReplanetizer.Utilities;

namespace Replanetizer.Frames
{
    public class LevelFrame : Frame
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        protected override string frameName { get; set; } = "Level";
        public Level level { get; set; }

        private List<TerrainFragment> terrains = new List<TerrainFragment>();
        private List<Tuple<Model,int,int>> collisions = new List<Tuple<Model, int, int>>();

        private static Vector4 normalColor = new Vector4(1, 1, 1, 1); // White
        private static Vector4 selectedColor = new Vector4(1, 0, 1, 1); // Purple

        public Matrix4 worldView { get; set; }

        public int shaderID { get; set; }
        public int colorShaderID { get; set; }
        public int collisionShaderID { get; set; }
        public int matrixID { get; set; }
        public int colorID { get; set; }

        private int uniformFogColorID;
        private int uniformFogNearDistID;
        private int uniformFogFarDistID;
        private int uniformFogNearIntensityID;
        private int uniformFogFarIntensityID;
        private int uniformUseFogID;

        private Matrix4 projection { get; set; }
        private Matrix4 view { get; set; }

        private int currentSplineVertex;
        public LevelObject selectedObject;

        private Vector2 mousePos;
        private Vector3 prevMouseRay;
        private int lastMouseX, lastMouseY;
        private bool xLock, yLock, zLock;

        public bool initialized, invalidate;
        public bool[] selectedChunks;
        public bool enableMoby = true, enableTie = true, enableShrub = true, enableSpline = false,
            enableCuboid = false, enableSpheres = false, enableCylinders = false, enableType0C = false, 
            enableSkybox = true, enableTerrain = true, enableCollision = false, enableTransparency = true, 
            enableFog = true;

        public Camera camera;
        private Tool currentTool;
        public Tool translateTool, rotationTool, scalingTool, vertexTranslator;

        public event EventHandler<RatchetEventArgs> ObjectClick;
        public event EventHandler<RatchetEventArgs> ObjectDeleted;

        private ConditionalWeakTable<IRenderable, BufferContainer> bufferTable;
        public Dictionary<Texture, int> textureIds;

        MemoryHook.MemoryHook hook;

        private List<int> collisionVbo = new List<int>();
        private List<int> collisionIbo = new List<int>();

        private int Width, Height;
        private int targetTexture, bufferTexture, framebufferId;

        public LevelFrame(Window wnd) : base(wnd)
        {
            bufferTable = new ConditionalWeakTable<IRenderable, BufferContainer>();
            CustomGLControl_Load();
        }

        public void RenderToFrameBuffer(Action renderFunction)
        {
            GL.DeleteTexture(targetTexture);
            targetTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, targetTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, Width, Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, (IntPtr) 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int) TextureMagFilter.Nearest);

            bufferTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, bufferTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, Width, Height, 0, PixelFormat.DepthComponent, PixelType.Float, (IntPtr) 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int) TextureMagFilter.Nearest);

            framebufferId = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebufferId);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, targetTexture, 0);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, bufferTexture, 0);

            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);

            GL.GenVertexArrays(1, out int VAO);
            GL.BindVertexArray(VAO);

            renderFunction();

            GL.DeleteFramebuffer(framebufferId);
            GL.DeleteTexture(bufferTexture);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void RenderMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("Level"))
                {
                    if (ImGui.MenuItem("Save"))
                    {
                        level.Save();
                    }

                    if (ImGui.MenuItem("Save as"))
                    {
                        var res = CrossFileDialog.SaveFile();
                        if (res.Length > 0)
                        {
                            level.Save(res);
                        }
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Render"))
                {
                    if (ImGui.Checkbox("Moby", ref enableMoby)) InvalidateView();
                    if (ImGui.Checkbox("Tie", ref enableTie)) InvalidateView();
                    if (ImGui.Checkbox("Shrub", ref enableShrub)) InvalidateView();
                    if (ImGui.Checkbox("Spline", ref enableSpline)) InvalidateView();
                    if (ImGui.Checkbox("Cuboid", ref enableCuboid)) InvalidateView();
                    if (ImGui.Checkbox("Spheres", ref enableSpheres)) InvalidateView();
                    if (ImGui.Checkbox("Cylinders", ref enableCylinders)) InvalidateView();
                    if (ImGui.Checkbox("Type0C", ref enableType0C)) InvalidateView();
                    if (ImGui.Checkbox("Skybox", ref enableSkybox)) InvalidateView();
                    if (ImGui.Checkbox("Terrain", ref enableTerrain)) InvalidateView();
                    if (ImGui.Checkbox("Collision", ref enableCollision)) InvalidateView();
                    if (ImGui.Checkbox("Transparency", ref enableTransparency)) InvalidateView();
                    if (ImGui.Checkbox("Fog", ref enableFog)) InvalidateView();
                    
                    ImGui.EndMenu();
                }

                if (selectedChunks.Length > 0)
                {
                    if (ImGui.BeginMenu("Chunks"))
                    {
                        for (int i = 0; i < selectedChunks.Length; i++)
                        {
                            if (ImGui.Checkbox("Chunk " + i, ref selectedChunks[i])) setSelectedChunks();
                        }
                        
                        ImGui.EndMenu();
                    }
                }
                
                ImGui.EndMenuBar();
            }
        }
        
        public override void Render(float deltaTime)
        {
            var viewport = ImGui.GetMainViewport();
            var pos = viewport.Pos;
            var size = viewport.Size;
            
            ImGui.SetNextWindowPos(pos);
            ImGui.SetNextWindowSize(size);
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 0);
            
            ImGui.Begin(frameName, 
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | 
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus | 
                ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking);
            
            ImGui.PopStyleVar(2);

            
            if (level != null)
            {
                RenderMenuBar();
                
                int prevWidth = Width, prevHeight = Height;

                System.Numerics.Vector2 vMin = ImGui.GetWindowContentRegionMin();
                System.Numerics.Vector2 vMax = ImGui.GetWindowContentRegionMax();

                Width = (int) (vMax.X - vMin.X);
                Height = (int) (vMax.Y - vMin.Y);

                if (Width <= 0 || Height <= 0) return; 

                if (Width != prevWidth || Height != prevHeight)
                {
                    invalidate = true;
                }

                System.Numerics.Vector2 windowPos = ImGui.GetWindowPos();
                Vector2 windowZero = new Vector2(windowPos.X + vMin.X, windowPos.Y + vMin.Y);
                mousePos = wnd.MousePosition - windowZero;

                Tick(deltaTime);

                if (invalidate)
                {
                    RenderToFrameBuffer(() => {
                        //Setup openGL variables
                        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
                        GL.Enable(EnableCap.DepthTest);
                        GL.LineWidth(5.0f);
                        GL.Viewport(0, 0, Width, Height);
                        
                        OnPaint();
                    });

                    invalidate = false;
                }
                ImGui.Image((IntPtr) targetTexture, new System.Numerics.Vector2(Width, Height), 
                    System.Numerics.Vector2.UnitY, System.Numerics.Vector2.UnitX);
            }

            ImGui.End();
        }

        private void CustomGLControl_Load()
        {
            GL.GenVertexArrays(1, out int VAO);
            GL.BindVertexArray(VAO);

            //Setup openGL variables
            GL.ClearColor(Color.SkyBlue);
            GL.Enable(EnableCap.DepthTest);
            GL.LineWidth(5.0f);

            //Setup general shader
            shaderID = GL.CreateProgram();
            LoadShader("Shaders/vs.glsl", ShaderType.VertexShader, shaderID);
            LoadShader("Shaders/fs.glsl", ShaderType.FragmentShader, shaderID);
            GL.LinkProgram(shaderID);

            //Setup color shader
            colorShaderID = GL.CreateProgram();
            LoadShader("Shaders/colorshadervs.glsl", ShaderType.VertexShader, colorShaderID);
            LoadShader("Shaders/colorshaderfs.glsl", ShaderType.FragmentShader, colorShaderID);
            GL.LinkProgram(colorShaderID);

            //Setup color shader
            collisionShaderID = GL.CreateProgram();
            LoadShader("Shaders/collisionshadervs.glsl", ShaderType.VertexShader, collisionShaderID);
            LoadShader("Shaders/collisionshaderfs.glsl", ShaderType.FragmentShader, collisionShaderID);
            GL.LinkProgram(collisionShaderID);

            matrixID = GL.GetUniformLocation(shaderID, "MVP");
            colorID = GL.GetUniformLocation(colorShaderID, "incolor");

            uniformFogColorID = GL.GetUniformLocation(shaderID, "fogColor");
            uniformFogNearDistID = GL.GetUniformLocation(shaderID, "fogNearDistance");
            uniformFogFarDistID = GL.GetUniformLocation(shaderID, "fogFarDistance");
            uniformFogNearIntensityID = GL.GetUniformLocation(shaderID, "fogNearIntensity");
            uniformFogFarIntensityID = GL.GetUniformLocation(shaderID, "fogFarIntensity");
            uniformUseFogID = GL.GetUniformLocation(shaderID, "useFog");

            projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 3, (float)Width / Height, 0.1f, 10000.0f);

            camera = new Camera();

            translateTool = new TranslationTool();
            rotationTool = new RotationTool();
            scalingTool = new ScalingTool();
            vertexTranslator = new VertexTranslationTool();

            initialized = true;
        }

        private void loadTexture(Texture t)
        {
            int texId;
            GL.GenTextures(1, out texId);
            GL.BindTexture(TextureTarget.Texture2D, texId);
            int offset = 0;

            if (t.mipMapCount > 1)
            {
                int mipWidth = t.width;
                int mipHeight = t.height;

                for (int mipLevel = 0; mipLevel < t.mipMapCount; mipLevel++)
                {
                    if (mipWidth > 0 && mipHeight > 0)
                    {
                        int size = ((mipWidth + 3) / 4) * ((mipHeight + 3) / 4) * 16;
                        byte[] texPart = new byte[size];
                        Array.Copy(t.data, offset, texPart, 0, size);
                        GL.CompressedTexImage2D(TextureTarget.Texture2D, mipLevel, InternalFormat.CompressedRgbaS3tcDxt5Ext, mipWidth, mipHeight, 0, size, texPart);
                        offset += size;
                        mipWidth /= 2;
                        mipHeight /= 2;
                    }
                }
            }
            else
            {
                int size = ((t.width + 3) / 4) * ((t.height + 3) / 4) * 16;
                GL.CompressedTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.CompressedRgbaS3tcDxt5Ext, t.width, t.height, 0, size, t.data);
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }

            textureIds.Add(t, texId);
        }

        void LoadLevelTextures()
        {
            textureIds = new Dictionary<Texture, int>();
            foreach (Texture t in level.textures)
            {
                loadTexture(t);
            }

            foreach (List<Texture> list in level.armorTextures)
            {
                foreach (Texture t in list)
                {
                    loadTexture(t);
                }
            }

            foreach (Texture t in level.gadgetTextures)
            {
                loadTexture(t);
            }

            foreach (Mission mission in level.missions)
            {
                foreach (Texture t in mission.textures)
                {
                    loadTexture(t);
                }
            }
        }

        private void LoadSingleCollisionBO(Collision col)
        {
            int id;
            GL.GenBuffers(1, out id);
            GL.BindBuffer(BufferTarget.ArrayBuffer, id);
            GL.BufferData(BufferTarget.ArrayBuffer, col.vertexBuffer.Length * sizeof(float), col.vertexBuffer, BufferUsageHint.StaticDraw);

            collisionVbo.Add(id);

            GL.GenBuffers(1, out id);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, id);
            GL.BufferData(BufferTarget.ElementArrayBuffer, col.indBuff.Length * sizeof(int), col.indBuff, BufferUsageHint.StaticDraw);

            collisionIbo.Add(id);
        }

        void LoadCollisionBOs()
        {
            foreach (int id in collisionIbo)
            {
                GL.DeleteBuffer(id);
            }

            foreach (int id in collisionVbo)
            {
                GL.DeleteBuffer(id);
            }

            collisionVbo.Clear();
            collisionIbo.Clear();

            if (level.collisionChunks.Count == 0)
            {
                LoadSingleCollisionBO((Collision)level.collisionEngine);
            } else
            {
                foreach (Model collisionModel in level.collisionChunks)
                {
                    LoadSingleCollisionBO((Collision)collisionModel);
                }
            }
        }

        public void LoadLevel(Level level)
        {
            this.level = level;

            GL.ClearColor(level.levelVariables.fogColor);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            level.skybox.textureConfig.Sort((emp1, emp2) => emp1.start.CompareTo(emp2.start));

            LoadLevelTextures();
            LoadCollisionBOs();

            Array.Resize(ref selectedChunks, level.collisionChunks.Count);
            for (int i = 0; i < level.collisionChunks.Count; i++)
            {
                selectedChunks[i] = true;
            }
            setSelectedChunks();
            
            Moby ratchet = level.mobs[0];
            camera.MoveBehind(ratchet);     
            SelectObject(null);
        }

        public void setSelectedChunks()
        {
            if (level.terrainChunks.Count == 0)
            {
                terrains.Clear();
                terrains.AddRange(level.terrainEngine);
                collisions.Clear();
                collisions.Add(new Tuple<Model,int,int>(level.collisionEngine, collisionVbo[0], collisionIbo[0]));
            } else
            {
                terrains.Clear();
                collisions.Clear();

                for (int i = 0; i < level.terrainChunks.Count; i++)
                {
                    if (selectedChunks[i])
                        terrains.AddRange(level.terrainChunks[i]);
                }

                for (int i = 0; i < level.collisionChunks.Count; i++)
                {
                    if (selectedChunks[i])
                        collisions.Add(new Tuple<Model, int, int>(level.collisionChunks[i], collisionVbo[i], collisionIbo[i]));
                }
            }
        }

        public void SelectObject(LevelObject newObject = null)
        {
            if (newObject == null)
            {
                selectedObject = null;
                InvalidateView();
                return;
            }

            if ((selectedObject is Spline) && !(newObject is Spline))
            {
                //Previous object was spline, new isn't
                if (currentTool is VertexTranslationTool) SelectTool(null);
            }

            selectedObject = newObject;

            ObjectClick?.Invoke(this, new RatchetEventArgs
            {
                Object = newObject
            });

            InvalidateView();
        }

        public void DeleteObject(LevelObject levelObject)
        {
            SelectObject(null);
            ObjectDeleted?.Invoke(this, new RatchetEventArgs
            {
                Object = levelObject
            });
            InvalidateView();
        }

        private void HandleMouseWheelChanges()
        {
            if (!(selectedObject is Spline spline)) return;
            if (!(currentTool is VertexTranslationTool)) return;
            
            int delta = (int) wnd.MouseState.ScrollDelta.Length / 120;
            if (delta > 0)
            {
                if (currentSplineVertex < spline.GetVertexCount() - 1)
                {
                    currentSplineVertex += 1;
                }
            }
            else if (currentSplineVertex > 0)
            {
                currentSplineVertex -= 1;
            }
            InvalidateView();
        }

        public void CloneMoby(Moby moby)
        {
            if (!(moby.Clone() is Moby newMoby)) return;

            level.mobs.Add(newMoby);
            SelectObject(newMoby);
            InvalidateView();
        }

        private void CustomGLControl_KeyDown()
        {
            /*
            switch (e.KeyCode)
            {
                case Keys.D1:
                    SelectTool(translateTool);
                    break;
                case Keys.D2:
                    SelectTool(rotationTool);
                    break;
                case Keys.D3:
                    SelectTool(scalingTool);
                    break;
                case Keys.D4:
                    SelectTool(vertexTranslator);
                    break;
                case Keys.D5:
                    SelectTool();
                    break;
                case Keys.Delete:
                    DeleteObject(selectedObject);
                    break;
            }
            */
        }


        public void SelectTool(Tool tool = null)
        {
            //enableTranslateTool = (tool is TranslationTool);
            //enableRotateTool = (tool is RotationTool);
            //enableScaleTool = (tool is ScalingTool);
            //enableSplineTool = (tool is VertexTranslationTool);

            currentTool = tool;

            currentSplineVertex = 0;
            InvalidateView();
        }

        public void Tick(float deltaTime)
        {
            if (!ImGui.IsWindowFocused())
            {
                return;
            }
            
            HandleMouseWheelChanges();

            float moveSpeed = wnd.IsKeyDown(Keys.LeftShift) ? 40 : 10;
            if (wnd.MouseState.IsButtonDown(MouseButton.Right))
            {
                camera.rotation.Z -= (wnd.MousePosition.X - lastMouseX) * camera.speed * deltaTime;
                camera.rotation.X -= (wnd.MousePosition.Y - lastMouseY) * camera.speed * deltaTime;
                camera.rotation.X = MathHelper.Clamp(camera.rotation.X, 
                    MathHelper.DegreesToRadians(-89.9f), MathHelper.DegreesToRadians(89.9f));

                Logger.Trace("Rotation, X: {0}, Y: {1}, Z: {2}", 
                    camera.rotation.X, camera.rotation.Y, camera.rotation.Z);
                InvalidateView();
            }
            
            if (wnd.MouseState.IsButtonDown(MouseButton.Left))
            {
                RenderToFrameBuffer(() =>
                {
                    LevelObject obj = GetObjectAtScreenPosition(mousePos, out bool cancelSelection);
                    if (cancelSelection) return;
                    SelectObject(obj);
                });
            }
            
            Vector3 moveDir = GetInputAxes();
            if (moveDir.Length > 0)
            {
                moveDir *= moveSpeed * deltaTime;
                InvalidateView();
                
            }

            lastMouseX = (int) wnd.MousePosition.X;
            lastMouseY = (int) wnd.MousePosition.Y;
            
            if (!invalidate) return;
            
            camera.TransformedTranslate(moveDir);
            Logger.Trace("Position, X: {0}, Y: {1}, Z: {2}", 
                camera.position.X, camera.position.Y, camera.position.Z);

            view = camera.GetViewMatrix();

            Vector3 mouseRay = MouseToWorldRay(projection, view, new Size(Width, Height), mousePos);
            prevMouseRay = mouseRay;

            if (xLock || yLock || zLock)
            {
                Vector3 direction = Vector3.Zero;
                if (xLock) direction = Vector3.UnitX;
                else if (yLock) direction = Vector3.UnitY;
                else if (zLock) direction = Vector3.UnitZ;
                float magnitudeMultiplier = 20;
                Vector3 magnitude = (mouseRay - prevMouseRay) * magnitudeMultiplier;


                switch (currentTool)
                {
                    case TranslationTool t:
                        selectedObject.Translate(direction * magnitude);
                        break;
                    case RotationTool t:
                        selectedObject.Rotate(direction * magnitude);
                        break;
                    case ScalingTool t:
                        selectedObject.Scale(direction * magnitude + Vector3.One);
                        break;
                    case VertexTranslationTool t:
                        if (selectedObject is Spline spline)
                        {
                            /*spline.TranslateVertex(currentSplineVertex, direction * magnitude);
                            //write at 0x346BA1180 + 0xC0 + spline.offset + currentSplineVertex * 0x10;
                            // List of splines 0x300A51BE0

                            byte[] ptrBuff = new byte[0x04];
                            int bytesRead = 0;
                            ReadProcessMemory(processHandle, 0x300A51BE0 + level.splines.IndexOf(spline) * 0x04, ptrBuff, ptrBuff.Length, ref bytesRead);
                            long splinePtr = ReadUint(ptrBuff, 0) + 0x300000010;

                            byte[] buff = new byte[0x0C];
                            Vector3 vec = spline.GetVertex(currentSplineVertex);
                            WriteFloat(buff, 0x00, vec.X);
                            WriteFloat(buff, 0x04, vec.Y);
                            WriteFloat(buff, 0x08, vec.Z);

                            WriteProcessMemory(processHandle, splinePtr + currentSplineVertex * 0x10, buff, buff.Length, ref bytesRead);*/
                        }
                        break;
                }

                /*
                if (wnd.MousePosition.Y < 10)
                {
                    wnd.MousePosition = new Vector2(wnd.MousePosition.X, Height - 10);
                    mouseRay = MouseToWorldRay(projection, view, new Size(Width, Height), new Vector2(wnd.MousePosition.X, Height - 10));
                } else if (wnd.MousePosition.Y > Height - 10)
                {
                    wnd.MousePosition = new Vector2(wnd.MousePosition.X, 10);
                    mouseRay = MouseToWorldRay(projection, view, new Size(Width, Height), new Vector2(wnd.MousePosition.X, 10));
                }

                if (wnd.MousePosition.X < 10)
                {
                    wnd.MousePosition = new Vector2(Width - 10, wnd.MousePosition.Y);
                    mouseRay = MouseToWorldRay(projection, view, new Size(Width, Height), new Vector2(Width - 10, wnd.MousePosition.Y));
                } else if (wnd.MousePosition.X > Width - 10)
                {
                    wnd.MousePosition = new Vector2(10, wnd.MousePosition.Y);
                    mouseRay = MouseToWorldRay(projection, view, new Size(Width, Height), new Vector2(10, wnd.MousePosition.Y));
                }
                */

                InvalidateView();
            }

        }

        private Vector3 GetInputAxes()
        {
            float xAxis = 0, yAxis = 0, zAxis = 0;

            if (wnd.KeyboardState.IsKeyDown(Keys.W)) yAxis++;
            if (wnd.KeyboardState.IsKeyDown(Keys.S)) yAxis--;
            if (wnd.KeyboardState.IsKeyDown(Keys.A)) xAxis--;
            if (wnd.KeyboardState.IsKeyDown(Keys.D)) xAxis++;
            if (wnd.KeyboardState.IsKeyDown(Keys.Q)) zAxis--;
            if (wnd.KeyboardState.IsKeyDown(Keys.E)) zAxis++;

            return new Vector3(xAxis, yAxis, zAxis);
        }

        public void FakeDrawSplines(List<Spline> splines, int offset)
        {
            for (int i = 0; i < splines.Count; i++)
            {
                Spline spline = splines[i];
                GL.UseProgram(colorShaderID);
                GL.EnableVertexAttribArray(0);
                Matrix4 worldView = this.worldView;
                GL.UniformMatrix4(matrixID, false, ref worldView);
                this.worldView = worldView;

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));

                ActivateBuffersForModel(spline);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, spline.vertexBuffer.Length / 3);
            }
        }


        public void FakeDrawCuboids(List<Cuboid> cuboids, int offset)
        {
            for (int i = 0; i < cuboids.Count; i++)
            {
                Cuboid cuboid = cuboids[i];

                GL.UseProgram(colorShaderID);
                GL.EnableVertexAttribArray(0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                Matrix4 mvp = cuboid.modelMatrix * worldView;
                GL.UniformMatrix4(matrixID, false, ref mvp);

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));

                ActivateBuffersForModel(cuboid);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

                GL.DrawElements(PrimitiveType.Triangles, Cuboid.cubeElements.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        public void FakeDrawSpheres(List<Sphere> spheres, int offset)
        {
            for (int i = 0; i < spheres.Count; i++)
            {
                Sphere sphere = spheres[i];

                GL.UseProgram(colorShaderID);
                GL.EnableVertexAttribArray(0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                Matrix4 mvp = sphere.modelMatrix * worldView;
                GL.UniformMatrix4(matrixID, false, ref mvp);

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));

                ActivateBuffersForModel(sphere);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

                GL.DrawElements(PrimitiveType.Triangles, Sphere.sphereTris.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        public void FakeDrawCylinders(List<Cylinder> cylinders, int offset)
        {
            for (int i = 0; i < cylinders.Count; i++)
            {
                Cylinder cylinder = cylinders[i];

                GL.UseProgram(colorShaderID);
                GL.EnableVertexAttribArray(0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                Matrix4 mvp = cylinder.modelMatrix * worldView;
                GL.UniformMatrix4(matrixID, false, ref mvp);

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));

                ActivateBuffersForModel(cylinder);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

                GL.DrawElements(PrimitiveType.Triangles, Cylinder.cylinderTris.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        public void FakeDrawType0Cs(List<Type0C> type0Cs, int offset)
        {
            for (int i = 0; i < type0Cs.Count; i++)
            {
                Type0C type0C = type0Cs[i];

                GL.UseProgram(colorShaderID);
                GL.EnableVertexAttribArray(0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                Matrix4 mvp = type0C.modelMatrix * worldView;
                GL.UniformMatrix4(matrixID, false, ref mvp);

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));

                ActivateBuffersForModel(type0C);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

                GL.DrawElements(PrimitiveType.Triangles, Cuboid.cubeElements.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        public void FakeDrawObjects(List<ModelObject> levelObjects, int offset)
        {
            for (int i = 0; i < levelObjects.Count; i++)
            {
                ModelObject levelObject = levelObjects[i];

                if (levelObject.model == null || levelObject.model.vertexBuffer == null)
                    continue;

                Matrix4 mvp = levelObject.modelMatrix * worldView;  //Has to be done in this order to work correctly
                GL.UniformMatrix4(matrixID, false, ref mvp);

                ActivateBuffersForModel(levelObject.model);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, 0);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 6);

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));
                GL.DrawElements(PrimitiveType.Triangles, levelObject.model.indexBuffer.Length, DrawElementsType.UnsignedShort, 0);

            }
        }

        public void ActivateBuffersForModel(IRenderable renderable)
        {
            BufferContainer container = bufferTable.GetValue(renderable, BufferContainer.FromRenderable);
            container.Bind();
        }

        public void RenderTool()
        {
            // Render tool on top of everything
            GL.Clear(ClearBufferMask.DepthBufferBit);

            if ((selectedObject != null) && (currentTool != null))
            {
                if ((currentTool is VertexTranslationTool) && (selectedObject is Spline spline))
                {
                    currentTool.Render(spline.GetVertex(currentSplineVertex), this);
                }
                else
                {
                    currentTool.Render(selectedObject.position, this);
                }
            }
        }

        void LoadShader(string filename, ShaderType type, int program)
        {
            int address = GL.CreateShader(type);
            using (StreamReader sr = new StreamReader(filename))
            {
                GL.ShaderSource(address, sr.ReadToEnd());
            }
            GL.CompileShader(address);
            GL.AttachShader(program, address);
            Logger.Debug("Compiled shader from {0}, log: {1}", filename, GL.GetShaderInfoLog(address));
        }

        protected void OnResize()
        {
            if (!initialized) return;
            GL.Viewport(0, 0, Width, Height);
            projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 3, (float)Width / Height, 0.1f, 10000.0f);
        }

        /*
        private void CustomGLControl_MouseDown()
        {
            rMouse = e.Button == MouseButtons.Right;
            lMouse = e.Button == MouseButtons.Left;

            if (e.Button == MouseButtons.Left && level != null)
            {
                LevelObject obj = GetObjectAtScreenPosition(e.Location.X, e.Location.Y, out bool cancelSelection);

                if (cancelSelection) return;

                SelectObject(obj);
            }
        }

        private void CustomGLControl_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            rMouse = false;
            lMouse = false;
            xLock = false;
            yLock = false;
            zLock = false;
        }
        */

        public LevelObject GetObjectAtScreenPosition(Vector2 pos, out bool hitTool)
        {
            LevelObject returnObject = null;
            int mobyOffset = 0, tieOffset = 0, shrubOffset = 0, splineOffset = 0, cuboidOffset = 0, sphereOffset = 0, cylinderOffset = 0, type0COffset = 0, tfragOffset = 0;
            
            GL.Viewport(0, 0, Width, Height);
            GL.Enable(EnableCap.DepthTest);
            GL.LineWidth(5.0f);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.UseProgram(colorShaderID);
            GL.EnableVertexAttribArray(0);

            worldView = view * projection;

            int offset = 0;


            if (enableMoby)
            {
                mobyOffset = offset;
                FakeDrawObjects(level.mobs.Cast<ModelObject>().ToList(), mobyOffset);
                offset += level.mobs.Count;
            }

            if (enableTie)
            {
                tieOffset = offset;
                FakeDrawObjects(level.ties.Cast<ModelObject>().ToList(), tieOffset);
                offset += level.ties.Count;
            }

            if (enableShrub)
            {
                shrubOffset = offset;
                FakeDrawObjects(level.shrubs.Cast<ModelObject>().ToList(), shrubOffset);
                offset += level.shrubs.Count;
            }

            if (enableSpline)
            {
                splineOffset = offset;
                FakeDrawSplines(level.splines, splineOffset);
                offset += level.splines.Count;
            }

            if (enableCuboid)
            {
                cuboidOffset = offset;
                FakeDrawCuboids(level.cuboids, cuboidOffset);
                offset += level.cuboids.Count;
            }

            if (enableSpheres)
            {
                sphereOffset = offset;
                FakeDrawSpheres(level.spheres, sphereOffset);
                offset += level.spheres.Count;
            }

            if (enableCylinders)
            {
                cylinderOffset = offset;
                FakeDrawCylinders(level.cylinders, cylinderOffset);
                offset += level.cylinders.Count;
            }

            if (enableType0C)
            {
                type0COffset = offset;
                FakeDrawType0Cs(level.type0Cs, type0COffset);
                offset += level.type0Cs.Count;
            }

            if (enableTerrain)
            {
                tfragOffset = offset;
                FakeDrawObjects(terrains.Cast<ModelObject>().ToList(), tfragOffset);
            }

            RenderTool();

            Pixel pixel = new Pixel();
            GL.ReadPixels((int) pos.X, Height - (int) pos.Y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, ref pixel);

            Logger.Trace("R: {0}, G: {1}, B: {2}, A: {3}", pixel.R, pixel.G, pixel.B, pixel.A);

            if (level != null && level.levelVariables != null)
                GL.ClearColor(level.levelVariables.fogColor);

            // Some GPU's put the alpha at 0, others at 255
            if (pixel.A == 255 || pixel.A == 0)
            {
                pixel.A = 0;

                bool didHitTool = false;
                if (pixel.R == 255 && pixel.G == 0 && pixel.B == 0)
                {
                    didHitTool = true;
                    xLock = true;
                }
                else if (pixel.R == 0 && pixel.G == 255 && pixel.B == 0)
                {
                    didHitTool = true;
                    yLock = true;
                }
                else if (pixel.R == 0 && pixel.G == 0 && pixel.B == 255)
                {
                    didHitTool = true;
                    zLock = true;
                }

                if (didHitTool)
                {
                    InvalidateView();
                    hitTool = true;
                    return null;
                }



                int id = (int)pixel.ToUInt32();
                if (enableMoby && id < level.mobs?.Count)
                {
                    returnObject = level.mobs[id];
                }
                else if (enableTie && id - tieOffset < level.ties.Count)
                {
                    returnObject = level.ties[id - tieOffset];
                }
                else if (enableShrub && id - shrubOffset < level.shrubs.Count)
                {
                    returnObject = level.shrubs[id - shrubOffset];
                }
                else if (enableSpline && id - splineOffset < level.splines.Count)
                {
                    returnObject = level.splines[id - splineOffset];
                }
                else if (enableCuboid && id - cuboidOffset < level.cuboids.Count)
                {
                    returnObject = level.cuboids[id - cuboidOffset];
                }
                else if (enableSpheres && id - sphereOffset < level.spheres.Count)
                {
                    returnObject = level.spheres[id - sphereOffset];
                }
                else if (enableCylinders && id - cylinderOffset < level.cylinders.Count)
                {
                    returnObject = level.cylinders[id - cylinderOffset];
                }
                else if (enableType0C && id - type0COffset < level.type0Cs.Count)
                {
                    returnObject = level.type0Cs[id - type0COffset];
                }
                else if (enableTerrain && id - tfragOffset < terrains.Count)
                {
                    returnObject = terrains[id - tfragOffset];
                }
            }

            hitTool = false;
            return returnObject;
        }


        void InvalidateView()
        {
            invalidate = true;
        }

        void RenderModelObject(ModelObject modelObject, bool selected)
        {
            if (modelObject.model == null || modelObject.model.vertexBuffer == null || modelObject.model.textureConfig.Count == 0) return;
            Matrix4 mvp = modelObject.modelMatrix * worldView;  //Has to be done in this order to work correctly
            GL.UniformMatrix4(matrixID, false, ref mvp);
            ActivateBuffersForModel(modelObject);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, 0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 6);

            //Bind textures one by one, applying it to the relevant vertices based on the index array
            foreach (TextureConfig conf in modelObject.model.textureConfig)
            {
                GL.BindTexture(TextureTarget.Texture2D, (conf.ID > 0) ? textureIds[level.textures[conf.ID]] : 0);
                GL.DrawElements(PrimitiveType.Triangles, conf.size, DrawElementsType.UnsignedShort, conf.start * sizeof(ushort));
            }

            if (selected)
            {
                bool switchBlends = enableTransparency && (modelObject is Moby);

                if (switchBlends)
                    GL.Disable(EnableCap.Blend);

                GL.UseProgram(colorShaderID);
                GL.Uniform4(colorID, new Vector4(1, 1, 1, 1));
                GL.UniformMatrix4(matrixID, false, ref mvp);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.DrawElements(PrimitiveType.Triangles, modelObject.model.indexBuffer.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.UseProgram(shaderID);

                if (switchBlends)
                    GL.Enable(EnableCap.Blend);
            }
        }

        public bool TryRPCS3Hook()
        {
            if (level == null || level.game == null) return false;

            hook = new MemoryHook.MemoryHook(level.game.num);

            return hook.hookWorking;
        }

        public bool RPCS3HookStatus()
        {
            if (hook != null && hook.hookWorking) return true;

            return false;
        }

        public void RemoveRPCS3Hook()
        {
            hook = null;
        }

        protected void OnPaint()
        {
            OnResize();

            worldView = view * projection;
            
            if (level != null && level.levelVariables != null)
                GL.ClearColor(level.levelVariables.fogColor);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            
            GL.UseProgram(shaderID);
            if (level != null && level.levelVariables != null)
            {
                GL.Uniform4(uniformFogColorID, level.levelVariables.fogColor);
                GL.Uniform1(uniformFogNearDistID, level.levelVariables.fogNearDistance);
                GL.Uniform1(uniformFogFarDistID, level.levelVariables.fogFarDistance);
                GL.Uniform1(uniformFogNearIntensityID, level.levelVariables.fogNearIntensity / 255.0f);
                GL.Uniform1(uniformFogFarIntensityID, level.levelVariables.fogFarIntensity / 255.0f);
                GL.Uniform1(uniformUseFogID, (enableFog) ? 1 : 0); 
            }

            if (enableSkybox)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.Disable(EnableCap.DepthTest);
                Matrix4 mvp = view.ClearTranslation() * projection;
                GL.UniformMatrix4(matrixID, false, ref mvp);
                ActivateBuffersForModel(level.skybox);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, 0);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 6);
                foreach (TextureConfig conf in level.skybox.textureConfig)
                {
                    GL.BindTexture(TextureTarget.Texture2D, (conf.ID > 0) ? textureIds[level.textures[conf.ID]] : 0);
                    GL.DrawElements(PrimitiveType.Triangles, conf.size, DrawElementsType.UnsignedShort, conf.start * sizeof(ushort));
                }
                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Blend);
            }

            if (enableTerrain)
                foreach (TerrainFragment tFrag in terrains)
                    RenderModelObject(tFrag, tFrag == selectedObject);

            if (enableShrub)
                foreach (Shrub shrub in level.shrubs)
                    RenderModelObject(shrub, shrub == selectedObject);

            if (enableTie)
                foreach (Tie tie in level.ties)
                    RenderModelObject(tie, tie == selectedObject);


            if (enableMoby)
            {
                if (hook != null) hook.UpdateMobys(level.mobs, level.mobyModels);

                if (enableTransparency)
                {
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                }

                foreach (Moby mob in level.mobs)
                {
                    RenderModelObject(mob, mob == selectedObject);
                }

                GL.Disable(EnableCap.Blend);
            }

            GL.UseProgram(colorShaderID);

            if (enableSpline)
                foreach (Spline spline in level.splines)
                {
                    var worldView = this.worldView;
                    GL.UniformMatrix4(matrixID, false, ref worldView);
                    GL.Uniform4(colorID, spline == selectedObject ? selectedColor : normalColor);
                    ActivateBuffersForModel(spline);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
                    GL.DrawArrays(PrimitiveType.LineStrip, 0, spline.vertexBuffer.Length / 3);
                }

            if (enableCuboid)
                foreach (Cuboid cuboid in level.cuboids)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    Matrix4 mvp = cuboid.modelMatrix * worldView;
                    GL.UniformMatrix4(matrixID, false, ref mvp);
                    GL.Uniform4(colorID, selectedObject == cuboid ? selectedColor : normalColor);
                    ActivateBuffersForModel(cuboid);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
                    GL.DrawElements(PrimitiveType.Triangles, Cuboid.cubeElements.Length, DrawElementsType.UnsignedShort, 0);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }

            if (enableSpheres)
                foreach (Sphere sphere in level.spheres)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    Matrix4 mvp = sphere.modelMatrix * worldView;
                    GL.UniformMatrix4(matrixID, false, ref mvp);
                    GL.Uniform4(colorID, selectedObject == sphere ? selectedColor : normalColor);
                    ActivateBuffersForModel(sphere);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
                    GL.DrawElements(PrimitiveType.Triangles, Sphere.sphereTris.Length, DrawElementsType.UnsignedShort, 0);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }

            if (enableCylinders)
                foreach (Cylinder cylinder in level.cylinders)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    Matrix4 mvp = cylinder.modelMatrix * worldView;
                    GL.UniformMatrix4(matrixID, false, ref mvp);
                    GL.Uniform4(colorID, selectedObject == cylinder ? selectedColor : normalColor);
                    ActivateBuffersForModel(cylinder);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
                    GL.DrawElements(PrimitiveType.Triangles, Cylinder.cylinderTris.Length, DrawElementsType.UnsignedShort, 0);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }

            if (enableType0C)
                foreach (Type0C type0c in level.type0Cs)
                {
                    GL.UseProgram(colorShaderID);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    Matrix4 mvp = type0c.modelMatrix * worldView;
                    GL.UniformMatrix4(matrixID, false, ref mvp);
                    GL.Uniform4(colorID, type0c == selectedObject ? selectedColor : normalColor);

                    ActivateBuffersForModel(type0c);

                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

                    GL.DrawElements(PrimitiveType.Triangles, Type0C.cubeElements.Length, DrawElementsType.UnsignedShort, 0);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }

            if (enableCollision)
            {
                for (int i = 0; i < collisions.Count; i++)
                {
                    Collision col = (Collision)collisions[i].Item1;
                    int vbo = collisions[i].Item2;
                    int ibo = collisions[i].Item3;

                    if (col.indBuff.Length == 0) continue;

                    GL.UseProgram(colorShaderID);
                    Matrix4 worldView = this.worldView;
                    GL.UniformMatrix4(matrixID, false, ref worldView);
                    GL.Uniform4(colorID, new Vector4(1, 1, 1, 1));

                    GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 4, 0);
                    GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, false, sizeof(float) * 4, sizeof(float) * 3);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);

                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    GL.DrawElements(PrimitiveType.Triangles, col.indBuff.Length, DrawElementsType.UnsignedInt, 0);
                    GL.UseProgram(collisionShaderID);
                    GL.UniformMatrix4(matrixID, false, ref worldView);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                    GL.DrawElements(PrimitiveType.Triangles, col.indBuff.Length, DrawElementsType.UnsignedInt, 0);
                }
            }

            RenderTool();

            GL.DisableVertexAttribArray(0);
            GL.DisableVertexAttribArray(1);
        }
    }

    public class RatchetEventArgs : EventArgs
    {
        public LevelObject Object { get; set; }
    }

}