import * as React from "react"

import { uploadProductImage } from "@/lib/api/quote-catalog"

// Base API (come client.ts): in dev è "" → stessa origine, /uploads è proxato a :5150.
const API_BASE = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") ?? ""

// TinyMCE 5 self-hosted: stessi asset del client WPF, copiati in public/tinymce.
// Riproduce fedelmente Assets/tinymce/editor.html (config, upload, tabella 50/50).
const TINYMCE_SRC = "/tinymce/tinymce.min.js"

// ── Tipi minimi per TinyMCE caricato via <script> (non da npm) ──
interface TinyMceBlobInfo {
  blob: () => Blob
  filename: () => string
}
interface TinyMceEditor {
  getContent: () => string
  setContent: (html: string) => void
  on: (event: string, handler: () => void) => void
  remove: () => void
}
interface TinyMceGlobal {
  init: (config: Record<string, unknown>) => Promise<TinyMceEditor[]>
}
declare global {
  interface Window {
    tinymce?: TinyMceGlobal
  }
}

let tinymceLoader: Promise<TinyMceGlobal> | null = null

/** Inietta lo <script> di TinyMCE una sola volta e risolve quando è pronto. */
function loadTinyMce(): Promise<TinyMceGlobal> {
  if (window.tinymce) return Promise.resolve(window.tinymce)
  if (tinymceLoader) return tinymceLoader
  tinymceLoader = new Promise<TinyMceGlobal>((resolve, reject) => {
    const script = document.createElement("script")
    script.src = TINYMCE_SRC
    script.referrerPolicy = "origin"
    script.onload = () => {
      if (window.tinymce) resolve(window.tinymce)
      else reject(new Error("TinyMCE non disponibile dopo il caricamento"))
    }
    script.onerror = () => {
      tinymceLoader = null
      reject(new Error("Impossibile caricare TinyMCE"))
    }
    document.head.appendChild(script)
  })
  return tinymceLoader
}

// URL immagini → relativi al salvataggio (toglie scheme://host davanti a /uploads/cms/).
function toRelativeUploads(html: string): string {
  return (html || "").replace(/src="https?:\/\/[^"/]+(\/uploads\/cms\/)/gi, 'src="$1')
}

// URL immagini → assoluti per la vista (no-op se API_BASE è vuoto = stessa origine).
function toDisplayUploads(html: string): string {
  const rel = toRelativeUploads(html || "")
  if (!API_BASE) return rel
  return rel.replace(/src="(\/uploads\/cms\/)/gi, `src="${API_BASE}$1`)
}

export interface RichTextEditorHandle {
  /** HTML corrente con gli URL immagine normalizzati a relativi (per il salvataggio). */
  getContent: () => string
}

interface RichTextEditorProps {
  /** Contenuto iniziale (HTML). Usato solo al primo init, come SetContent del WPF. */
  initialValue?: string
  /** Altezza dell'area editor in px. */
  height?: number
}

/**
 * Editor descrizione prodotto: TinyMCE 5 in React, fedele a editor.html del WPF.
 * Le immagini sono caricate sul server (POST /products/upload) e referenziate con
 * path RELATIVO /uploads/cms/products/...; tabelle 1×2 al 50/50 restano integre.
 */
export const RichTextEditor = React.forwardRef<
  RichTextEditorHandle,
  RichTextEditorProps
>(function RichTextEditor({ initialValue = "", height = 360 }, ref) {
  const targetRef = React.useRef<HTMLTextAreaElement | null>(null)
  const editorRef = React.useRef<TinyMceEditor | null>(null)
  const initialRef = React.useRef(initialValue)
  initialRef.current = initialValue
  const heightRef = React.useRef(height)
  heightRef.current = height

  const [error, setError] = React.useState<string | null>(null)
  const [loading, setLoading] = React.useState(true)

  React.useImperativeHandle(ref, () => ({
    getContent: () => {
      if (editorRef.current) {
        return toRelativeUploads(editorRef.current.getContent())
      }
      // Fallback (TinyMCE non caricato): leggi la textarea grezza.
      return targetRef.current?.value ?? initialRef.current
    },
  }))

  React.useEffect(() => {
    let disposed = false
    let editor: TinyMceEditor | null = null

    loadTinyMce()
      .then((tinymce) => {
        // loadTinyMce è async: in StrictMode la cleanup del primo mount è già
        // passata qui (disposed=true) → non inizializzare l'editor "morto".
        if (disposed || !targetRef.current) return
        return tinymce.init({
          target: targetRef.current,
          height: heightRef.current,
          menubar: "file edit view insert format table tools",
          branding: false,
          promotion: false,
          statusbar: true,
          resize: false,
          // Niente conversione URL: persistiamo path relativi /uploads/cms/ come il WPF.
          relative_urls: false,
          convert_urls: false,
          plugins: [
            "advlist autolink lists link image charmap print preview anchor",
            "searchreplace visualblocks code fullscreen",
            "insertdatetime media table paste code help wordcount",
            "imagetools hr nonbreaking pagebreak textcolor colorpicker",
          ],
          toolbar: [
            "undo redo | formatselect | bold italic underline strikethrough | forecolor backcolor",
            "alignleft aligncenter alignright alignjustify | bullist numlist outdent indent | table image link | hr blockquote | code fullscreen",
          ],
          content_style:
            "body { font-family: Segoe UI, sans-serif; font-size: 13px; margin: 10px; }",
          image_advtab: true,
          image_caption: true,
          paste_data_images: true,
          automatic_uploads: true,
          // Upload di TUTTE le immagini (incolla/trascina/file picker) sul server.
          images_upload_handler: (
            blobInfo: TinyMceBlobInfo,
            success: (url: string) => void,
            failure: (message: string) => void
          ) => {
            uploadProductImage(blobInfo.blob(), blobInfo.filename() || "image.png")
              .then((relativePath) => success(API_BASE + relativePath))
              .catch((err: Error) => failure(err.message || "Upload fallito"))
          },
          file_picker_types: "image",
          object_resizing: true,
          resize_img_proportional: true,
          table_default_attributes: { border: "1" },
          table_default_styles: {
            "border-collapse": "collapse",
            width: "100%",
          },
          table_responsive_width: true,
          setup: (ed: TinyMceEditor) => {
            editor = ed
            ed.on("init", () => {
              if (disposed) {
                ed.remove()
                return
              }
              editorRef.current = ed
              ed.setContent(toDisplayUploads(initialRef.current))
              setLoading(false)
            })
          },
        })
      })
      .catch((err: Error) => {
        if (!disposed) {
          setError(err.message)
          setLoading(false)
        }
      })

    return () => {
      disposed = true
      const ed = editorRef.current ?? editor
      editorRef.current = null
      if (ed) {
        try {
          ed.remove()
        } catch {
          // ignora errori di teardown
        }
      }
    }
  }, [])

  if (error) {
    // Fallback: textarea semplice se TinyMCE non si carica (editor non disponibile).
    return (
      <div className="space-y-1">
        <p className="text-xs text-destructive">Editor non disponibile: {error}</p>
        <textarea
          ref={targetRef}
          defaultValue={initialRef.current}
          style={{ minHeight: height }}
          className="w-full rounded-md border p-2 font-mono text-sm"
        />
      </div>
    )
  }

  return (
    <div className="relative">
      {loading ? (
        <div className="pointer-events-none absolute inset-0 z-10 flex items-center justify-center text-sm text-muted-foreground">
          Caricamento editor…
        </div>
      ) : null}
      <textarea ref={targetRef} defaultValue={initialRef.current} />
    </div>
  )
})
