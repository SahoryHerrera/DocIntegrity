import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { GoogleOAuthProvider } from "@react-oauth/google";
// App.jsx (inicio de sesión original) se conserva como referencia para la etapa 6.
import HuellaApp from "./huella/HuellaApp.jsx";

const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID;

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <GoogleOAuthProvider clientId={googleClientId}>
      <HuellaApp />
    </GoogleOAuthProvider>
  </StrictMode>
);