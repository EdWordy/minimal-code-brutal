#version 450

// One axis-aligned rectangle per draw. The quad is generated from gl_VertexIndex,
// so there is no vertex buffer at all.
layout(push_constant) uniform Quad
{
    vec4 rect;      // x, y, width, height in playfield units (origin top-left)
    vec4 color;     // linear RGBA
    vec2 playfield; // playfield size in units
} pc;

layout(location = 0) out vec4 outColor;

void main()
{
    // Two triangles: (0,0) (1,0) (0,1) and (0,1) (1,0) (1,1).
    const vec2 corners[6] = vec2[](
        vec2(0, 0), vec2(1, 0), vec2(0, 1),
        vec2(0, 1), vec2(1, 0), vec2(1, 1));

    vec2 units = pc.rect.xy + corners[gl_VertexIndex] * pc.rect.zw;
    vec2 ndc = units / pc.playfield * 2.0 - 1.0; // Vulkan NDC: y points down, matching our units

    gl_Position = vec4(ndc, 0.0, 1.0);
    outColor = pc.color;
}
