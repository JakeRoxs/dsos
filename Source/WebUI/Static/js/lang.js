// lang.js
// By GoBlock2021

// Define default language
const defaultLang = "en-US";

function saveLanguage(lang) {
    localStorage.setItem("userLang", lang);
    loadLanguage(lang);
}

function loadLanguage(lang) {
    // Try to load the specified language JSON file
    fetch(`./locales/${lang}.json`)
        .then((response) => {
            // Check response status
            if (!response.ok) {
                throw new Error("Invalid locales file!");
            }
            return response.json();
        })
        .then((data) => {
            // Update page text
            updatePageText(data);
        })
        .catch((error) => {
            // Print error message to console
            console.error(
                "There was a problem fetching the language file:",
                error,
            );

            // Fallback to default language
            if (lang != defaultLang) {
                loadLanguage(defaultLang);
            }
        });
}

function updatePageText(data) {
    // Iterate over all elements with the data-i18n attribute
    document.querySelectorAll("[data-i18n]").forEach((element) => {
        const key = element.getAttribute("data-i18n");
        // Ensure the key exists in data, otherwise use the element's original text content
        // Using textContent avoids interpreting the translation as HTML, which prevents XSS.
        element.textContent = data[key] || element.textContent;
    });

    // notify any listeners that translation has been applied
    window.dispatchEvent(new Event("lang-updated"));
}

function init() {
    var browserLang = navigator.language || navigator.userLanguage;
    var userLang = localStorage.getItem("userLang");
    if (userLang == null) {
        userLang = browserLang;
        saveLanguage(browserLang);
    }
    loadLanguage(userLang);
}

window.addEventListener("DOMContentLoaded", (event) => {
    init();
});

function generateJson() {
    // 这个函数可以用来生成一个JS模板用作本地化，如果需要，请直接在浏览器控制台执行
    // This function can be used to generate a JS template for localization.
    // Please execute it directly in the browser console if needed.

    // Use textContent here to match how updatePageText applies translations.
    // This avoids accidentally including HTML markup in the generated JSON.
    const translations = {};

    // Iterate over all elements with the data-i18n attribute
    document.querySelectorAll("[data-i18n]").forEach((element) => {
        const key = element.getAttribute("data-i18n");
        translations[key] = element.textContent;
    });

    // Print a well-formed JSON object that can be copied into a locale file
    console.log(JSON.stringify(translations, null, 2));
}
