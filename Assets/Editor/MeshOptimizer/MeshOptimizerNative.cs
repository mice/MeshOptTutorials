using System;
using System.Runtime.InteropServices;

namespace MeshOptimizer
{
    internal static class MeshOptimizerNative
    {
        private const string MeshOptimizerDLL = "meshoptimizer.dll";
        
        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint meshopt_generateVertexRemap(uint[] Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize);
        
        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern void meshopt_remapIndexBuffer(uint[] Destination, uint[] Indices, UIntPtr IndexCount, uint[] Remap);
        
        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern void meshopt_remapVertexBuffer(IntPtr Destination, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize, uint[] Remap);
        
        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern void meshopt_optimizeVertexCache(uint[] Destination, uint[] Indices, UIntPtr IndexCount, UIntPtr VertexCount);
        
        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern void meshopt_optimizeOverdraw(uint[] Destination, uint[] Indices, UIntPtr IndexCount, IntPtr VertexPositions, UIntPtr VertexCount, UIntPtr Stride, float Threshold);

        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint meshopt_optimizeVertexFetch(IntPtr Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize);

        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint meshopt_simplify(IntPtr Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize,UIntPtr target_index_count,float target_error,uint options,out float result_error);
        //inline size_t meshopt_simplify(T* destination, const T* indices, size_t index_count, const float* vertex_positions, size_t vertex_count, size_t vertex_positions_stride, size_t target_index_count, float target_error, unsigned int options = 0, float* result_error = NULL);




        /**
         * Generate index buffer that can be used for more efficient rendering when only a subset of the vertex attributes is necessary
         * All vertices that are binary equivalent (wrt first vertex_size bytes) map to the first vertex in the original vertex buffer.
         * This makes it possible to use the index buffer for Z pre-pass or shadowmap rendering, while using the original index buffer for regular rendering.
         * Note that binary equivalence considers all vertex_size bytes, including padding which should be zero-initialized.
         *
         * destination must contain enough space for the resulting index buffer (index_count elements)
         */
        //MESHOPTIMIZER_API void meshopt_generateShadowIndexBuffer(unsigned int* destination, const unsigned int* indices, size_t index_count, const void* vertices, size_t vertex_count, size_t vertex_size, size_t vertex_stride);
        [DllImport(MeshOptimizerDLL, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl)]
        private static extern void meshopt_generateShadowIndexBuffer(IntPtr Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize, UIntPtr VertexStride);

        public static uint GenerateVertexRemap(uint[] Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices,
            UIntPtr VertexCount, UIntPtr VertexSize)
        {
            return meshopt_generateVertexRemap(Destination, Indices, IndexCount, Vertices, VertexCount, VertexSize);
        }
        
        public static void RemapIndexBuffer(uint[] Destination, uint[] Indices, UIntPtr IndexCount, uint[] Remap)
        {
            meshopt_remapIndexBuffer(Destination, Indices, IndexCount, Remap);
        }
        
        public static void RemapVertexBuffer(IntPtr Destination, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize, uint[] Remap)
        {
            meshopt_remapVertexBuffer(Destination, Vertices, VertexCount, VertexSize, Remap);
        }
        
        public static void OptimizeVertexCache(uint[] Destination, uint[] Indices, UIntPtr IndexCount, UIntPtr VertexCount)
        {
            meshopt_optimizeVertexCache(Destination, Indices, IndexCount, VertexCount);
        }
        
        public static void OptimizeOverdraw(uint[] Destination, uint[] Indices, UIntPtr IndexCount, IntPtr VertexPositions, UIntPtr VertexCount, UIntPtr Stride, float Threshold)
        {
            meshopt_optimizeOverdraw(Destination, Indices, IndexCount, VertexPositions, VertexCount, Stride, Threshold);
        }
        
        public static uint OptimizeVertexFetch(IntPtr Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize)
        {
            return meshopt_optimizeVertexFetch(Destination, Indices, IndexCount, Vertices, VertexCount, VertexSize);
        }

        public static uint Simplify(IntPtr Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize,UIntPtr target_index_count,float target_error, uint options,out float result_error)
        {
            return meshopt_simplify(Destination, Indices, IndexCount, Vertices, VertexCount, VertexSize, target_index_count, target_error, options, out result_error);
        }

        public static void GenerateShadowIndexBuffer(IntPtr Destination, uint[] Indices, UIntPtr IndexCount, IntPtr Vertices, UIntPtr VertexCount, UIntPtr VertexSize, UIntPtr VertexStride)
        {
            meshopt_generateShadowIndexBuffer(Destination, Indices, IndexCount, Vertices, VertexCount, VertexSize, VertexStride);
        }
    }
}