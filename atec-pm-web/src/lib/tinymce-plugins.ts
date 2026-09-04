/**
 * I plugin TinyMCE dell'editor descrizioni (`components/shared/RichTextEditor.tsx`).
 *
 * Una lista sola per due usi: `lib/tinymce.ts` li importa uno per uno (così finiscono
 * nel bundle) e `RichTextEditor` la passa a `tinymce.init`. Il test accanto
 * (`tinymce.test.ts`) pretende che le due cose combacino: un plugin nominato ma non
 * importato darebbe a runtime un 404 silenzioso e un bottone in meno, uno importato
 * ma non nominato sarebbe peso morto nel chunk.
 *
 * Rispetto alla config della 5 mancano `paste`, `hr`, `textcolor`, `colorpicker` e
 * `print` (dalla 6 sono nel core), `imagetools` (diventato a pagamento) e `help`
 * (il menu Help non è nella menubar: era irraggiungibile).
 */
export const TINYMCE_PLUGINS = [
  "advlist",
  "autolink",
  "lists",
  "link",
  "image",
  "charmap",
  "preview",
  "anchor",
  "searchreplace",
  "visualblocks",
  "code",
  "fullscreen",
  "insertdatetime",
  "media",
  "table",
  "wordcount",
  "nonbreaking",
  "pagebreak",
] as const
