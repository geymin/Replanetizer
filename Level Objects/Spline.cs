﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static RatchetEdit.DataFunctions;
using OpenTK.Graphics.OpenGL;

namespace RatchetEdit
{
    public class Spline : LevelObject
    {
        public struct Vertex {
            public float id;
            public float x, y, z;
            public Vertex(float id, float x, float y, float z) {
                this.id = id;
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public override string ToString() {
                return String.Format("ID: {0} X: {1} Y: {2} Z:{3}", id, x, y, z);
            }
        }
        public List<Vertex> vertices = new List<Vertex>();
        public int name;
        public float[] vertexBuffer;

        int VBO;

        public Spline(byte[] splineBlock, int offset)
        {
            name = offset;
            int count = ReadInt(splineBlock, offset);
            vertexBuffer = new float[count * 3];
            for(int i = 0; i < count; i++)
            {
                float x = vertexBuffer[(i * 3) + 0] = ReadFloat(splineBlock, offset + 0x10 + (i * 0x10) + 0x00);
                float y = vertexBuffer[(i * 3) + 1] = ReadFloat(splineBlock, offset + 0x10 + (i * 0x10) + 0x04);
                float z = vertexBuffer[(i * 3) + 2] = ReadFloat(splineBlock, offset + 0x10 + (i * 0x10) + 0x08);
                vertices.Add(new Vertex(i, x, y, z));
                //vertexBuffer[i] = ReadFloat(splineBlock, offset + (i * 0x10));
                //Console.WriteLine(String.Format("X: {0} Y: {1} Z:{2}", vertexBuffer[(i * 3) + 0], vertexBuffer[(i * 3) + 1], vertexBuffer[(i * 3) + 2]));
            }

            if(count > 0) {
                position = new OpenTK.Vector3(vertices[0].x, vertices[0].y, vertices[0].z);
            }
        }

        public void getVBO()
        {
            //Get the vertex buffer object, or create one if one doesn't exist
            if (VBO == 0)
            {
                GL.GenBuffers(1, out VBO);
                GL.BindBuffer(BufferTarget.ArrayBuffer, VBO);
                GL.BufferData(BufferTarget.ArrayBuffer, vertexBuffer.Length * sizeof(float), vertexBuffer, BufferUsageHint.StaticDraw);
                Console.WriteLine("Generated VBO with ID: " + VBO.ToString());
            }
            else
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, VBO);
                GL.BufferData(BufferTarget.ArrayBuffer, vertexBuffer.Length * sizeof(float), vertexBuffer, BufferUsageHint.StaticDraw);
            }
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        }
    }
}
