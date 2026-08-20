/**
 * Quale chat si sta guardando in questo momento (id) — informazione volutamente FUORI da React:
 * la legge la campanella in testata, che vive in un altro ramo dell'albero, per decidere se il
 * riquadro «hai un messaggio» ha senso. Un contesto avrebbe imposto un provider attorno a tutta
 * l'app per un solo numero che cambia a ogni clic sulla lista.
 */
let activeChatId: number | null = null

export function setActiveChatId(chatId: number | null): void {
  activeChatId = chatId
}

export function getActiveChatId(): number | null {
  return activeChatId
}
