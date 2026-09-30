import 'package:flutter/material.dart';

import 'api.dart';
import 'app_state.dart';
import 'screens/home_screen.dart';
import 'screens/login_screen.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await AppState.instance.load();
  Api.instance.onSignedOut = AppState.instance.markSignedOut;
  runApp(const PosApp());
}

class PosApp extends StatelessWidget {
  const PosApp({super.key});

  @override
  Widget build(BuildContext context) {
    const brand = Color(0xFF8F1D3F);
    return MaterialApp(
      title: 'Manoksha POS',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: brand, primary: brand),
        useMaterial3: true,
        inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder()),
      ),
      home: ListenableBuilder(listenable: AppState.instance, builder: (context, _) => AppState.instance.signedIn ? const HomeScreen() : const LoginScreen()),
    );
  }
}
