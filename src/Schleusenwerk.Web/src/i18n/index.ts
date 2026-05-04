import { createI18n } from 'vue-i18n'
import de from './de'
import en from './en'

export type SupportedLocale = 'de' | 'en'

const STORAGE_KEY = 'schleusenwerk-locale'

function getSavedLocale(): SupportedLocale {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'de' || saved === 'en') { return saved }
    const browserLang = navigator.language.slice(0, 2)
    return browserLang === 'de' ? 'de' : 'en'
}

export const i18n = createI18n({
    legacy: false,
    locale: getSavedLocale(),
    fallbackLocale: 'en',
    messages: { de, en },
})

export function setLocale(locale: SupportedLocale) {
    i18n.global.locale.value = locale
    localStorage.setItem(STORAGE_KEY, locale)
    document.documentElement.lang = locale
}
