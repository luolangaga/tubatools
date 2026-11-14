import { Navigate, Route, Routes } from "react-router-dom";
import { MainLayout, type LayoutMode } from "./components/ui/main-layout";
import HomePage from "./pages/HomePage";
import ToolsPage from "./pages/ToolsPage";
import OptimizePage from "./pages/OptimizePage";
import DisplayFilterPage from "./pages/DisplayFilterPage";
import HardwarePage from "./pages/HardwarePage";
import SettingsPage from "./pages/SettingsPage";
import AboutPage from "./pages/AboutPage";
import DeltaForcePage from "./pages/DeltaForcePage";
import OtherGunCodePlatformsPage from "./pages/OtherGunCodePlatformsPage";
import DeltaForceRoulettePage from "./pages/DeltaForceRoulettePage";
import StreamerDirectoryPage from "./pages/StreamerDirectoryPage";
import StreamerProfilePage from "./pages/StreamerProfilePage";

function readMode(): LayoutMode {
  const mode = new URLSearchParams(window.location.search).get("mode");
  return mode === "skin" ? "skin" : "embedded";
}

export default function App() {
  const mode = readMode();
  return (
    <MainLayout mode={mode}>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/hardware" element={<HardwarePage />} />
        <Route path="/tools" element={<ToolsPage />} />
        <Route path="/optimize" element={<OptimizePage />} />
        <Route path="/display-filter" element={<DisplayFilterPage />} />
        <Route path="/delta-force" element={<DeltaForcePage />} />
        <Route path="/delta-force/other-platforms" element={<OtherGunCodePlatformsPage />} />
        <Route path="/delta-force/random-equipment" element={<DeltaForceRoulettePage />} />
        <Route path="/delta-force/streamers" element={<StreamerDirectoryPage />} />
        <Route path="/delta-force/streamers/:id" element={<StreamerProfilePage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </MainLayout>
  );
}
