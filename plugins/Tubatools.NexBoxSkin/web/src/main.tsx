import React, { useEffect, useState } from "react";
import ReactDOM from "react-dom/client";
import { ChakraProvider, ColorModeScript, Spinner, Center } from "@chakra-ui/react";
import { CacheProvider } from "@emotion/react";
import createCache from "@emotion/cache";
import { HashRouter } from "react-router-dom";
import { I18nextProvider } from "react-i18next";
import App from "./App";
import "./index.css";
import i18n from "./lib/i18n";
import theme from "./lib/theme";
import { BackgroundProvider } from "./contexts/background-context";
import { ThemeColorProvider } from "./contexts/theme-color-context";
import { ThemeModeProvider } from "./contexts/theme-mode-context";

const emotionCache = createCache({ key: "css", prepend: true });

function Root() {
  const [ready, setReady] = useState(i18n.isInitialized);

  useEffect(() => {
    if (ready) return;
    const handler = () => setReady(true);
    i18n.on("initialized", handler);
    return () => i18n.off("initialized", handler);
  }, [ready]);

  if (!ready) {
    return (
      <Center h="100vh">
        <Spinner size="xl" color="teal.300" />
      </Center>
    );
  }

  return (
    <React.StrictMode>
      <CacheProvider value={emotionCache}>
        <ColorModeScript initialColorMode={theme.config.initialColorMode} />
        <I18nextProvider i18n={i18n}>
          <ChakraProvider theme={theme}>
            <HashRouter>
              <ThemeColorProvider>
                <BackgroundProvider>
                  <ThemeModeProvider>
                    <App />
                  </ThemeModeProvider>
                </BackgroundProvider>
              </ThemeColorProvider>
            </HashRouter>
          </ChakraProvider>
        </I18nextProvider>
      </CacheProvider>
    </React.StrictMode>
  );
}

ReactDOM.createRoot(document.getElementById("root")!).render(<Root />);
