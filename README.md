# FodyTools
A set of helper functions to ease building of [Fody](https://github.com/Fody/Fody/) weavers

Use this repo as a submodule, and include (link) the source files you need in your weaver project.

Do not build an assembly from this code, unless you give it a very unique name! 
Shipping an assembly with your weaver, also if it's hidden because you embedded it using Costura, would break other weavers using the same assembly in a different version.

