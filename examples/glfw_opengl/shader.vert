#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;
uniform mat4 uProjection;
out vec3 outColor;

void main()
{
    gl_Position = uProjection * vec4(aPos, 1.0);
    outColor = aColor;
}
