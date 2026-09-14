Platform: maccatalyst-arm64
Configuration: Release
QWENTTS_REPO: https://github.com/SeasonRealms/qwentts.cpp
QWENTTS_REF: master
GGML_REPO: https://github.com/SeasonRealms/ggml
GGML_REF: master
Target triple: arm64-apple-ios15.0-macabi
Linkage: static (QWEN_SHARED=OFF, GGML_BACKEND_DL=OFF), merged into a single libqwen.dylib
Enabled backends: cpu (armv8.2-a+dotprod), metal, blas=true

Mac Catalyst qwentts artifact contents:
Apple Silicon only: no x86_64 slice is produced, so this runtime does not load on Intel Macs. Add a maccatalyst-x64 build if Intel support is needed again.
Static linkage: qwen-core, ggml and every backend are archived into libqwen.dylib and registered at runtime by ggml's backend registry; nothing is dlopen'd, so the app bundle contains a single Mach-O to sign.
CPU targets the M1 baseline (armv8.2-a+dotprod). GGML_CPU_ALL_VARIANTS is intentionally NOT used because it requires GGML_BACKEND_DL; M1 and every later Apple Silicon chip run this baseline.
Metal uses GGML_METAL_EMBED_LIBRARY so the shader source travels inside libqwen.dylib; no default.metallib has to be deployed or located in the app bundle.
Metal targets the Apple Silicon unified-memory GPU only; Intel integrated and AMD discrete GPUs are out of scope for this artifact.
CUDA and Vulkan are unavailable on Mac Catalyst and are explicitly disabled.

libqwen.dylib sha256: 55a3fdb943ebde1b5e6eef6150456678bbd7aa5e6693dc484e2546ecb1b0e27d
Artifact contents:
ggml.txt
libqwen.dylib
qwen.h
qwentts.txt
README.txt
