# VL.Rive

Support for [Rive](https://rive.app/) files in VL including bi-directional [Data Binding](https://rive.app/docs/editor/data-binding/overview).

For use with vvvv, the visual live-programming environment for .NET: http://vvvv.org

## Improvements in this fork
Compared to the original [vvvv/VL.Rive](https://github.com/vvvv/VL.Rive), this fork adds:
- **Newer Rive** – updated to the latest (2026-08) Rive runtime, so newer editor features and fixes are supported.
- **Set text by name** – a *Text Runs* input on `RiveRenderer` lets you change named text directly, without setting up data binding first.
- **Animation scrubbing** – drive an animation to an exact position/time yourself instead of only playing it.
- **External time control** – advance the whole scene (including nested artboards) on your own clock, for frame-accurate and reproducible timing.
- **More robust** – switch view model and scene in the same frame, an explicit update trigger, and safer artboard/scene lookup that fails gracefully instead of erroring.

## Getting started
- Install as [described here](https://thegraybook.vvvv.org/reference/hde/managing-nugets.html) via commandline:

    `nuget install VL.Rive -pre`

- Usage examples and more information are included in the pack and can be found via the [Help Browser](https://thegraybook.vvvv.org/reference/hde/findinghelp.html)

## Contributing
- Report issues on [the vvvv forum](https://forum.vvvv.org/c/vvvv-gamma/28)
- For custom development requests, please [get in touch](mailto:devvvvs@vvvv.org)
- When making a pull-request, please make sure to read the general [guidelines on contributing to vvvv libraries](https://thegraybook.vvvv.org/reference/extending/contributing.html)

## Credits
Based on the [Low-level C++ Rive runtime and renderer](https://github.com/rive-app/rive-runtime)

## Sponsoring
Development of this library was partially sponsored by:  
* [Refik Anadol Studio](https://refikanadolstudio.com/)
