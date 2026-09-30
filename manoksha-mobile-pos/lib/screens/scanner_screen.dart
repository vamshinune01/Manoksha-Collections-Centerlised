import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

/// Camera barcode scanning; returns the first code read.
class ScannerScreen extends StatefulWidget {
  const ScannerScreen({super.key});

  @override
  State<ScannerScreen> createState() => _ScannerScreenState();
}

class _ScannerScreenState extends State<ScannerScreen> {
  bool _done = false;

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Scan barcode')),
    body: MobileScanner(
      onDetect: (capture) {
        final code = capture.barcodes.firstOrNull?.rawValue;
        if (_done || code == null || code.isEmpty) return;
        _done = true;
        Navigator.of(context).pop(code);
      },
    ),
  );
}
