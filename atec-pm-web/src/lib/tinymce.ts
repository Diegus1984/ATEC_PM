/**
 * TinyMCE 8 da npm, impacchettato da Vite (dal 04/09/2026; prima: 5.10.9 vendorizzata in
 * `public/tinymce`, 190 file fuori supporto dal 2023).
 *
 * Questo è l'UNICO modulo che importa l'editor. `RichTextEditor` lo carica con un
 * `import()`: tutto TinyMCE (core, tema, plugin, skin ≈ 1 MB) sta in un chunk a parte,
 * scaricato solo quando si apre un dialogo con l'editor, non a ogni avvio dell'app.
 *
 * L'ORDINE degli import conta:
 * 1. `tinymce` per primo — mette `window.tinymce`, che tema, icone, modello e plugin
 *    usano per registrarsi (sono file CommonJS che si appoggiano al globale);
 * 2. gli `skin.js` — registrano il CSS in `tinymce.Resource` con le chiavi
 *    `ui/oxide/skin.css`, `ui/oxide/content.css` e `content/default/content.css`: il tema
 *    le cerca lì PRIMA di provare a scaricare un file, quindi non serve alcun `skin_url`;
 * 3. i plugin — gli stessi di `TINYMCE_PLUGINS`, che il test accanto tiene allineati.
 *
 * Licenza: dalla 7 TinyMCE è GPLv2+ per chi lo ospita da sé (`license_key: 'gpl'` nella
 * config, altrimenti mostra un avviso); l'applicazione è a uso interno e non viene
 * distribuita, quindi la GPL non impone nulla.
 */
import tinymce from "tinymce"

import "tinymce/icons/default"
import "tinymce/themes/silver"
import "tinymce/models/dom"

import "tinymce/skins/ui/oxide/skin.js"
import "tinymce/skins/ui/oxide/content.js"
import "tinymce/skins/content/default/content.js"

import "tinymce/plugins/advlist"
import "tinymce/plugins/autolink"
import "tinymce/plugins/lists"
import "tinymce/plugins/link"
import "tinymce/plugins/image"
import "tinymce/plugins/charmap"
import "tinymce/plugins/preview"
import "tinymce/plugins/anchor"
import "tinymce/plugins/searchreplace"
import "tinymce/plugins/visualblocks"
import "tinymce/plugins/code"
import "tinymce/plugins/fullscreen"
import "tinymce/plugins/insertdatetime"
import "tinymce/plugins/media"
import "tinymce/plugins/table"
import "tinymce/plugins/wordcount"
import "tinymce/plugins/nonbreaking"
import "tinymce/plugins/pagebreak"

export default tinymce
