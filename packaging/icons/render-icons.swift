// Rasterises an SVG into PNGs at the icon sizes and packs a Windows .ico from them.
// Usage: swift render-icons.swift <icon.svg> <png-dir> <out.ico>
import AppKit

let args = CommandLine.arguments
guard args.count == 4, let svg = NSImage(contentsOf: URL(fileURLWithPath: args[1])) else {
    FileHandle.standardError.write("usage: render-icons.swift <icon.svg> <png-dir> <out.ico>\n".data(using: .utf8)!)
    exit(1)
}
let pngDir = URL(fileURLWithPath: args[2])
let icoSizes = [16, 24, 32, 48, 64, 128, 256]
var pngs: [Int: Data] = [:]

for size in Set([16, 32, 48, 64, 128, 256, 512, 1024] + icoSizes).sorted() {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    rep.size = NSSize(width: size, height: size)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    svg.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
    NSGraphicsContext.restoreGraphicsState()
    let data = rep.representation(using: .png, properties: [:])!
    pngs[size] = data
    if size != 24 {
        try data.write(to: pngDir.appendingPathComponent("hermes-\(size).png"))
    }
}

// ICO with PNG-compressed entries (supported since Windows Vista).
var ico = Data()
func le16(_ v: Int) { ico.append(contentsOf: [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF)]) }
func le32(_ v: Int) { le16(v & 0xFFFF); le16((v >> 16) & 0xFFFF) }
le16(0); le16(1); le16(icoSizes.count)
var offset = 6 + 16 * icoSizes.count
for size in icoSizes {
    let data = pngs[size]!
    ico.append(UInt8(size % 256)); ico.append(UInt8(size % 256)) // 256 is written as 0
    ico.append(0); ico.append(0)
    le16(1); le16(32)
    le32(data.count); le32(offset)
    offset += data.count
}
for size in icoSizes { ico.append(pngs[size]!) }
try ico.write(to: URL(fileURLWithPath: args[3]))
