Platform: ios-arm64 (xcframework carrying the device and arm64-simulator slices)
Configuration: Release
QWENTTS_REPO: https://github.com/SeasonRealms/qwentts.cpp
QWENTTS_REF: master
GGML_REPO: https://github.com/SeasonRealms/ggml
GGML_REF: master
Target triples: arm64-apple-ios17.0 (device), arm64-apple-ios17.0-simulator (simulator)
Linkage: static (QWEN_SHARED=OFF, GGML_BACKEND_DL=OFF), merged into a single libqwen.a per slice
Enabled backends: cpu (armv8.2-a+dotprod), metal, blas=true

iOS qwentts artifact contents:
Two slices only, both arm64: ios-arm64 (device) and ios-arm64-simulator (Apple Silicon Macs). One arm64 iOS target does NOT also cover the simulator - device and simulator are distinct Mach-O platforms; no x86_64 simulator slice is produced, Intel Macs are out of scope.
SeasonTTS declares the qwentts P/Invokes as __Internal on iOS (unlike the libqwen dylib used on Linux/Mac Catalyst), so the app statically links libqwen.a via NativeReference (Kind=Static).
Static linkage: qwen-core, ggml and every backend are archived into the single libqwen.a and registered at runtime by ggml's backend registry; nothing is dlopen'd.
CPU targets the M1 baseline (armv8.2-a+dotprod), supported by every A12+ device and every Apple Silicon simulator.
Metal uses GGML_METAL_EMBED_LIBRARY so the shader source travels inside libqwen.a; no default.metallib has to be deployed or located in the app bundle.
BLAS uses Apple's Accelerate framework (GGML_BLAS_VENDOR=Apple), which is available on both iOS devices and simulators.
CUDA and Vulkan are unavailable on iOS and are explicitly disabled.

libqwen.a sha256 (ios-arm64): aa9f85162653f2b79f9d43b29f79042996bc2be3b21b7f99957bbce095e4aa10
libqwen.a sha256 (ios-arm64-simulator): e920709800e1c8b10672555ff6b02df8b81d01144cdf8178bf574c03b78f5126
Artifact contents:
.
./ggml.txt
./libqwen.xcframework
./libqwen.xcframework/Info.plist
./libqwen.xcframework/ios-arm64
./libqwen.xcframework/ios-arm64-simulator
./libqwen.xcframework/ios-arm64-simulator/libqwen.a
./libqwen.xcframework/ios-arm64/libqwen.a
./qwen.h
./qwentts.txt
./README.txt
