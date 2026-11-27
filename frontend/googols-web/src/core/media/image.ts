// Photos for the AI teacher: shrunk in the browser to at most 1600 px and re-encoded as JPEG, so a phone
// photo of a textbook page uploads quickly and stays well under the server's 4 MB limit.

export const MAX_PHOTO_BYTES = 4 * 1024 * 1024

export interface PreparedPhoto { dataUrl: string; bytes: number }

const base64Bytes = (dataUrl: string) => Math.floor((dataUrl.length - dataUrl.indexOf(',') - 1) * 3 / 4)

function readAsDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const r = new FileReader()
    r.onload = () => resolve(String(r.result))
    r.onerror = () => reject(new Error("Couldn't read that photo."))
    r.readAsDataURL(file)
  })
}

export async function preparePhoto(file: File, maxSide = 1600): Promise<PreparedPhoto> {
  if (!file.type.startsWith('image/')) throw new Error('Choose a photo (JPEG or PNG).')
  try {
    const bitmap = await createImageBitmap(file)
    const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(bitmap.width * scale)
    canvas.height = Math.round(bitmap.height * scale)
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('no canvas')
    ctx.fillStyle = '#fff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    for (const quality of [0.85, 0.7, 0.55]) {
      const dataUrl = canvas.toDataURL('image/jpeg', quality)
      const bytes = base64Bytes(dataUrl)
      if (bytes <= MAX_PHOTO_BYTES * 0.85) return { dataUrl, bytes }
    }
    throw new Error('That photo is too large. Try a smaller one.')
  } catch (e) {
    if (e instanceof Error && e.message.startsWith('That photo')) throw e
    // Older browsers without createImageBitmap: send JPEG/PNG as they are if small enough.
    if (!/^image\/(jpeg|png)$/.test(file.type) || file.size > MAX_PHOTO_BYTES * 0.85) throw new Error('Use a JPEG or PNG photo under 3 MB.')
    const dataUrl = await readAsDataUrl(file)
    return { dataUrl, bytes: base64Bytes(dataUrl) }
  }
}
