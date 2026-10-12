import { useState } from "react";
import { GoogleLogin } from "@react-oauth/google";
import "./App.css";

function App() {
  const [user, setUser] = useState(null);
  const [error, setError] = useState("");

  const handleGoogleSuccess = async (credentialResponse) => {
    try {
      setError("");

      const response = await fetch(
        "http://localhost:5235/api/auth/google",
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            idToken: credentialResponse.credential,
          }),
        }
      );

      const data = await response.json();

      if (!response.ok) {
        throw new Error(
          data.message || "No se pudo iniciar sesión."
        );
      }

      localStorage.setItem("docintegrity_token", data.token);
      localStorage.setItem(
        "docintegrity_user",
        JSON.stringify(data.user)
      );

      setUser(data.user);

      console.log("Login correcto:", data);
    } catch (error) {
      console.error(error);
      setError(error.message);
    }
  };

  const handleGoogleError = () => {
    setError("No se pudo iniciar sesión con Google.");
  };

  const handleLogout = () => {
    localStorage.removeItem("docintegrity_token");
    localStorage.removeItem("docintegrity_user");

    setUser(null);
  };

  return (
    <main className="page">
      <section className="login-card">
        <h1>DocIntegrity</h1>

        <p>
          Verificación de integridad de documentos mediante
          Blockchain e Inteligencia Artificial
        </p>

        {!user ? (
          <>
            <GoogleLogin
              onSuccess={handleGoogleSuccess}
              onError={handleGoogleError}
            />

            {error && (
              <p className="error">
                {error}
              </p>
            )}
          </>
        ) : (
          <div className="user-section">
            {user.pictureUrl && (
              <img
                src={user.pictureUrl}
                alt={user.name}
                className="avatar"
              />
            )}

            <h2>¡Bienvenida, {user.name}!</h2>

            <p>{user.email}</p>

            <button onClick={handleLogout}>
              Cerrar sesión
            </button>
          </div>
        )}
      </section>
    </main>
  );
}

export default App;