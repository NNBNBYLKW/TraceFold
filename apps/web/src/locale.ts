export type UiLocale = 'en' | 'zh'

const LOCALE_STORAGE_KEY = 'tracefold.web.locale'

function normalizeLocale(value: string | null | undefined): UiLocale | null {
  if (!value) {
    return null
  }

  const normalized = value.toLowerCase()
  if (normalized.startsWith('zh')) {
    return 'zh'
  }
  if (normalized.startsWith('en')) {
    return 'en'
  }
  return null
}

function readStoredLocale(): UiLocale | null {
  try {
    return normalizeLocale(globalThis.localStorage?.getItem(LOCALE_STORAGE_KEY) ?? null)
  } catch {
    return null
  }
}

function detectBrowserLocale(): UiLocale | null {
  try {
    const languages =
      typeof navigator !== 'undefined' && Array.isArray(navigator.languages) && navigator.languages.length > 0
        ? navigator.languages
        : typeof navigator !== 'undefined' && navigator.language
          ? [navigator.language]
          : []

    for (const language of languages) {
      const locale = normalizeLocale(language)
      if (locale) {
        return locale
      }
    }
  } catch {
    return null
  }

  return null
}

export function getInitialLocale(): UiLocale {
  return readStoredLocale() ?? detectBrowserLocale() ?? 'en'
}

export function persistLocale(locale: UiLocale): void {
  try {
    globalThis.localStorage?.setItem(LOCALE_STORAGE_KEY, locale)
  } catch {
    // Ignore persistence failures and keep the in-memory locale.
  }
}
