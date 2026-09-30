import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api.dart';

class PosBranch {
  PosBranch(this.id, this.code, this.name, this.canOverridePrice, this.canApprove);

  factory PosBranch.fromJson(Map<String, dynamic> j) =>
      PosBranch(j['id'] as String, j['code'] as String, j['name'] as String, j['canOverridePrice'] as bool, j['canApprove'] as bool);

  final String id;
  final String code;
  final String name;
  final bool canOverridePrice;
  final bool canApprove;
}

/// Signed-in POS session: who, which branch, limits and accepted payment methods (all from the backend).
class AppState extends ChangeNotifier {
  AppState._();
  static final AppState instance = AppState._();

  bool signedIn = false;
  String userId = '';
  String displayName = '';
  bool isOwner = false;
  List<PosBranch> branches = [];
  PosBranch? branch;
  double staffMaxDiscountPct = 0;
  double managerMaxDiscountPct = 0;
  List<String> paymentMethods = [];

  // Printer (device preference only — never business data).
  String? printerMac;
  String? printerName;
  bool paper80mm = false;

  Future<void> load() async {
    final prefs = await SharedPreferences.getInstance();
    printerMac = prefs.getString('printerMac');
    printerName = prefs.getString('printerName');
    paper80mm = prefs.getBool('paper80mm') ?? false;
    if (await TokenStore.access() != null) {
      try {
        await refreshContext();
      } on ApiException {
        signedIn = false;
      }
    }
    notifyListeners();
  }

  Future<void> refreshContext() async {
    final c = await Api.instance.get('pos/context') as Map<String, dynamic>;
    userId = c['userId'] as String;
    displayName = c['displayName'] as String;
    isOwner = c['isOwner'] as bool;
    branches = (c['branches'] as List).map((b) => PosBranch.fromJson(b as Map<String, dynamic>)).toList();
    staffMaxDiscountPct = (c['staffMaxDiscountPct'] as num).toDouble();
    managerMaxDiscountPct = (c['managerMaxDiscountPct'] as num).toDouble();
    paymentMethods = (c['paymentMethods'] as List).cast<String>();
    final prefs = await SharedPreferences.getInstance();
    final saved = prefs.getString('branchId');
    branch = branches.where((b) => b.id == saved).firstOrNull ?? (branches.length == 1 ? branches.first : null);
    signedIn = true;
    notifyListeners();
  }

  Future<void> selectBranch(PosBranch b) async {
    branch = b;
    (await SharedPreferences.getInstance()).setString('branchId', b.id);
    notifyListeners();
  }

  Future<void> savePrinter(String? mac, String? name, bool wide) async {
    printerMac = mac;
    printerName = name;
    paper80mm = wide;
    final prefs = await SharedPreferences.getInstance();
    if (mac == null) {
      await prefs.remove('printerMac');
      await prefs.remove('printerName');
    } else {
      await prefs.setString('printerMac', mac);
      await prefs.setString('printerName', name ?? mac);
    }
    await prefs.setBool('paper80mm', wide);
    notifyListeners();
  }

  /// The session expired or was revoked on the server.
  void markSignedOut() {
    signedIn = false;
    notifyListeners();
  }

  Future<void> signOut() async {
    final refresh = await TokenStore.refresh();
    if (refresh != null) {
      try {
        await Api.instance.post('auth/logout', {'refreshToken': refresh});
      } catch (_) {}
    }
    await TokenStore.clear();
    signedIn = false;
    branch = null;
    notifyListeners();
  }
}
