# SPDX-License-Identifier: AGPL-3.0-only
# nota_media: Codec registry (WAV/FLAC/MP3), SampleBuffer, StreamingAudioSource, AudioImport, TempoDetect, SampleAnalysis, ClipWarpStream.

nota_add_shared_module(nota_media
    ALIAS media
    OUTPUT_NAME nota_media
    EXPORT_MACRO NOTA_MEDIA
    PUBLIC_HEADERS
        include/nota/media/Export.h
        include/nota/media/AudioCodecTypes.h
        include/nota/media/IAudioDecoder.h
        include/nota/media/IAudioEncoder.h
        include/nota/media/AudioCodecRegistry.h
        include/nota/media/AudioSource.h
        include/nota/media/SampleBuffer.h
        include/nota/media/StreamingAudioSource.h
        include/nota/media/AudioFile.h
        include/nota/media/AudioImport.h
        include/nota/media/TempoDetect.h
        include/nota/media/SampleAnalysis.h
        include/nota/media/Warp.h
    SOURCES
        src/codecs/PathUtils.cpp
        src/codecs/WavCodec.cpp
        src/codecs/FlacCodec.cpp
        src/codecs/Mp3Codec.cpp
        src/codecs/BuiltinCodecs.cpp
        src/AudioCodecRegistry.cpp
        src/SampleBuffer.cpp
        src/StreamingAudioSource.cpp
        src/AudioFile.cpp
        src/AudioImport.cpp
        src/TempoDetect.cpp
        src/SampleAnalysis.cpp
        src/WarpStream.cpp
)

target_link_libraries(nota_media
    PUBLIC  nota::math
    PRIVATE FLAC lame
)

target_include_directories(nota_media SYSTEM PRIVATE
    ${dr_libs_SOURCE_DIR}
    ${signalsmith_linear_SOURCE_DIR}/include
    ${signalsmith_stretch_SOURCE_DIR}/include
)
