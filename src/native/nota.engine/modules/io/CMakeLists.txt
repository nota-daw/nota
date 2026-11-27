# SPDX-License-Identifier: AGPL-3.0-only
# Nota hardware I/O module: audio, MIDI, and gamepad subsystems kept strictly
# mutually independent with platform vendor SDKs encapsulated privately.

set(NOTA_IO_PUBLIC_HEADERS
    include/nota/io/Export.h
    include/nota/io/audio/AudioIoTypes.h
    include/nota/io/audio/OutputRouter.h
    include/nota/io/audio/IAudioInputStream.h
    include/nota/io/audio/IAudioOutputStream.h
    include/nota/io/audio/IAudioDriver.h
    include/nota/io/audio/AudioDriverRegistry.h
    include/nota/io/audio/AudioConfigStore.h
    include/nota/io/audio/NullAudioDriver.h
    include/nota/io/audio/asio/AsioSampleFormat.h
    include/nota/io/audio/asio/AsioDriverApi.h
    include/nota/io/audio/asio/AsioSession.h
    include/nota/io/audio/asio/AsioDriverList.h
    include/nota/io/midi/MidiTypes.h
    include/nota/io/midi/MidiParser.h
    include/nota/io/midi/MpeInput.h
    include/nota/io/midi/IMidiInput.h
    include/nota/io/midi/IMidiOutput.h
    include/nota/io/midi/IMidiDriver.h
    include/nota/io/midi/MidiDriverRegistry.h
    include/nota/io/midi/NullMidiDriver.h
    include/nota/io/gamepad/GamepadTypes.h
    include/nota/io/gamepad/IGamepadDriver.h
    include/nota/io/gamepad/GamepadInput.h
)

set(NOTA_IO_SOURCES
    src/audio/NullAudioDriver.cpp
    src/audio/AudioDriverRegistry.cpp
    src/audio/AudioConfigStore.cpp
    src/audio/asio/AsioSampleFormat.cpp
    src/audio/asio/AsioSession.cpp
    src/audio/drivers/AsioDriverList_win.cpp
    src/midi/MpeInput.cpp
    src/midi/NullMidiDriver.cpp
    src/midi/MidiDriverRegistry.cpp
    src/gamepad/GamepadInput.cpp
)

if(APPLE)
    list(APPEND NOTA_IO_SOURCES
        src/audio/drivers/CoreAudioDriver_mac.mm
        src/midi/drivers/CoreMidiDriver_mac.mm
        src/gamepad/drivers/GameControllerDriver_mac.mm
    )
elseif(WIN32 OR (UNIX AND NOT APPLE))
    # Private vendor compilation units so third-party miniaudio / RtMidi code compiles
    # once without Nota's -Werror flags affecting upstream code.
    add_library(nota_io_vendor STATIC
        src/audio/drivers/miniaudio_impl.cpp
        ${rtmidi_SOURCE_DIR}/RtMidi.cpp
    )
    nota_apply_common_target_settings(nota_io_vendor)
    set_target_properties(nota_io_vendor PROPERTIES POSITION_INDEPENDENT_CODE ON)
    target_include_directories(nota_io_vendor
        PRIVATE
            "${CMAKE_CURRENT_SOURCE_DIR}/src"
        SYSTEM PRIVATE
            "${miniaudio_SOURCE_DIR}"
            "${rtmidi_SOURCE_DIR}"
    )
    if(WIN32)
        target_compile_definitions(nota_io_vendor PRIVATE __WINDOWS_MM__ NOMINMAX WIN32_LEAN_AND_MEAN _USE_MATH_DEFINES)
    elseif(UNIX AND NOT APPLE)
        target_compile_definitions(nota_io_vendor PRIVATE __LINUX_ALSA__)
    endif()
    if(NOTA_ENABLE_SANITIZERS)
        if(MSVC)
            target_compile_options(nota_io_vendor PRIVATE /fsanitize=address)
        elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
            target_compile_options(nota_io_vendor PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
        endif()
    endif()

    list(APPEND NOTA_IO_SOURCES
        src/audio/drivers/MiniaudioDriver.cpp
        src/midi/drivers/RtMidiDriver.cpp
        src/gamepad/drivers/GamepadStubDriver.cpp
    )
    if(WIN32)
        list(APPEND NOTA_IO_SOURCES
            src/audio/drivers/ComAsioDriver_win.cpp
            src/audio/drivers/AsioDriver_win.cpp
        )
    endif()
else()
    message(FATAL_ERROR "No Nota audio/MIDI/gamepad backend for this platform yet.")
endif()

nota_add_shared_module(nota_io
    ALIAS         io
    OUTPUT_NAME   nota_io
    EXPORT_MACRO  NOTA_IO
    PUBLIC_HEADERS ${NOTA_IO_PUBLIC_HEADERS}
    SOURCES        ${NOTA_IO_SOURCES}
)

if(APPLE)
    set_source_files_properties(
        src/audio/drivers/CoreAudioDriver_mac.mm
        src/midi/drivers/CoreMidiDriver_mac.mm
        src/gamepad/drivers/GameControllerDriver_mac.mm
        PROPERTIES
            COMPILE_OPTIONS "-Wno-old-style-cast;-Wno-unused-parameter"
    )
endif()

target_include_directories(nota_io
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src"
)

target_link_libraries(nota_io
    PUBLIC
        nota::dsp
        nota::math
    PRIVATE
        nota::core
        nlohmann_json::nlohmann_json
)

if(APPLE)
    target_link_libraries(nota_io PRIVATE
        "-framework AudioToolbox"
        "-framework CoreAudio"
        "-framework CoreMIDI"
        "-framework AudioUnit"
        "-framework Foundation"
        "-framework GameController"
        "-framework CoreFoundation"
    )
elseif(WIN32 OR (UNIX AND NOT APPLE))
    target_link_libraries(nota_io PRIVATE nota_io_vendor)
    target_include_directories(nota_io SYSTEM PRIVATE
        "${miniaudio_SOURCE_DIR}"
        "${rtmidi_SOURCE_DIR}"
    )
    if(WIN32)
        target_compile_definitions(nota_io PRIVATE
            __WINDOWS_MM__
            NOMINMAX
            WIN32_LEAN_AND_MEAN
            _USE_MATH_DEFINES
            NOTA_HAS_ASIO=1
        )
        target_include_directories(nota_io SYSTEM PRIVATE "${asiosdk_SOURCE_DIR}/common")
        target_link_libraries(nota_io PRIVATE winmm ole32 advapi32 user32 avrt)
    elseif(UNIX AND NOT APPLE)
        target_compile_definitions(nota_io PRIVATE __LINUX_ALSA__)
        target_link_libraries(nota_io PRIVATE asound pthread dl m)
    endif()
endif()
