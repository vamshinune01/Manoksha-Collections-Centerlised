import { SimulatorApp } from "./simulator-app";

export const metadata = { title: "UPI payment (simulator)", robots: { index: false } };

/** Development stand-in for the customer's UPI app. The backend refuses the simulator in Production. */
export default async function SimulatorPage({ params }: { params: Promise<{ ref: string }> }) {
  const { ref } = await params;
  return <SimulatorApp providerRef={ref} />;
}
